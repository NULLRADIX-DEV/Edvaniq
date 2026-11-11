using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.EventFlowTests;

// #211: an answer changes today's plan. Assessment grades it and sends AnswerRecorded, LearningEngine stores the
// learning record, and Planning, which works the plan out on every call with the mastery asked from LearningEngine,
// shows the new state. Nothing is stored or cached on the way, so the only wait is the broker.
[Collection(FlowCollection.Name)]
public sealed class PlanFlowTests(FlowServices services) : IDisposable
{
    private const string Text = """
        # Photosynthese
        ## Lichtreaktion
        Licht spaltet Wasser.

        ## Calvin-Zyklus
        CO2 wird zu Zucker.

        # Zellatmung
        Zucker wird zu Energie.
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IHost ai = SimulatedAi();

    [Fact(Timeout = 180_000)]
    public async Task RightAnswer_MovesItsTopicBackInTodaysPlan()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(alice);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topics = await TopicsWithQuestionsAsync(token, alice);

        // Every topic stands at LearningEngine's start of 50, so they weigh the same and the id decides. The topic of
        // the first learn block is the one the plan puts first.
        var before = await PlanAsync(token, cancellationToken);
        Assert.True(before.MasteryAvailable);
        var topic = before.Blocks.First(block => block.Kind == "learn").TopicId;
        Assert.Contains(topic, topics);

        await AnswerRightAsync(token, topic, cancellationToken);

        // A right answer pulls the mastery part of the way to 100: 50 to 62 for a middle question (57 to 67 by
        // difficulty), so its weight drops from 50 to 38 while the others stay at 50. The topic has to lose minutes
        // today or come up later, whatever else the plan looks like.
        await WaitForAsync(
            async () =>
            {
                var after = await PlanAsync(token, cancellationToken);
                return LearnMinutes(after, topic) < LearnMinutes(before, topic) || FirstLearnBlock(after, topic) > FirstLearnBlock(before, topic);
            },
            "Today's plan did not move the answered topic back.");
    }

    // #222, Q4: how long after an answer LearningEngine names it as the topic's newest, so mastery and today's plan
    // contain it. Measured from the grade to the first GET /mastery that has it, asked every 20 ms: Assessment's outbox,
    // NATS, LearningEngine's inbox and its database, all real. Not in it: the way between browser and server and the
    // client's own interval of 500 ms. Ten answers one after the other, the median has to stay under 5 s.
    [Fact(Timeout = 300_000)]
    public async Task Answer_ReachesMasteryAndPlan_TypicallyWithinFiveSeconds()
    {
        const int Answers = 10;
        var bob = $"bob-{Guid.NewGuid():N}";
        var token = TestTokens.Create(bob);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = (await TopicsWithQuestionsAsync(token, bob))[0];
        using var learningEngine = Client(services.LearningEngine.CreateClient(), token);

        var delays = new List<TimeSpan>();
        for (var i = 0; i < Answers; i++)
        {
            var answer = await AnswerRightAsync(token, topic, cancellationToken);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (await LastAnswerAsync(learningEngine, topic, cancellationToken) != answer)
            {
                Assert.True(watch.Elapsed < Timeout, $"Answer {i + 1} never reached LearningEngine.");
                await Task.Delay(20, cancellationToken);
            }

            delays.Add(watch.Elapsed);

            // The plan is worked out from that same mastery on every call, so it contains the answer now too.
            Assert.True((await PlanAsync(token, cancellationToken)).MasteryAvailable);
        }

        var sorted = delays.Order().ToList();
        var median = (sorted[(Answers / 2) - 1] + sorted[Answers / 2]) / 2;
        var measured = $"Q4, answer to mastery and plan, {Answers} answers: median {median.TotalMilliseconds:F0} ms, "
            + $"max {sorted[^1].TotalMilliseconds:F0} ms ({string.Join(", ", delays.Select(delay => $"{delay.TotalMilliseconds:F0}"))})";
        TestContext.Current.TestOutputHelper?.WriteLine(measured);
        Assert.True(median < TimeSpan.FromSeconds(5), $"Median {median.TotalMilliseconds:F0} ms, Q4 allows 5000 ms.");
    }

    public void Dispose() => ai.Dispose();

    private static async Task<Guid?> LastAnswerAsync(HttpClient learningEngine, Guid topic, CancellationToken cancellationToken)
    {
        using var response = await learningEngine.GetAsync($"/mastery/{topic}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var standing = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return standing.GetProperty("lastAnswerId") is { ValueKind: JsonValueKind.String } id ? id.GetGuid() : null;
    }

    private IChatClient Chat => ai.Services.GetRequiredService<IChatClient>();

    // The pasted text the whole way: topics in Knowledge, questions in Assessment, and Planning and LearningEngine know
    // every topic of the default goal.
    private async Task<List<Guid>> TopicsWithQuestionsAsync(string token, string user)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var content = Client(services.Content.CreateClient(), token);
        using var pasted = await content.PostAsJsonAsync("/documents", new { text = Text }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
        var documentId = (await pasted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();

        await ActivatorUtilities.CreateInstance<DocumentSplitter>(services.Content.Services).SplitAsync(documentId, cancellationToken);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(services.Content.Services, Chat).AnalyzeAsync(documentId, cancellationToken);
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(services.Content.Services, Chat);
        await WaitForAsync(async () => await pools.FinishAsync(documentId, cancellationToken), "The worker never got the topics from Knowledge.");

        var topics = services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters()
            .Where(topic => topic.DocumentId == documentId).Select(topic => topic.Id).ToList());
        Assert.NotEmpty(topics);
        await WaitForAsync(
            () => Task.FromResult(QuestionTopics(user).Count == topics.Count),
            "Assessment did not take the questions of every topic over.");
        foreach (var receiver in services.Receivers)
        {
            await WaitForAsync(
                () => Task.FromResult(receiver.KnownTopics(user).Count == topics.Count),
                $"{receiver} did not take the topics over.");
        }

        return topics;
    }

    // Asks a question of the topic and answers it right, with the solution read from Assessment's database.
    private async Task<Guid> AnswerRightAsync(string token, Guid topicId, CancellationToken cancellationToken)
    {
        using var assessment = Client(services.Assessment.CreateClient(), token);
        using var asked = await assessment.PostAsJsonAsync("/attempts", new { topicId }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var attempt = await asked.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var questionId = attempt.GetProperty("questionId").GetGuid();
        var question = services.QueryAssessment(db => db.Questions.IgnoreQueryFilters().Single(item => item.Id == questionId));

        object answer = question.Type == QuestionType.MultipleChoice
            ? new { options = question.Answers, responseTimeMs = 5000 }
            : new { option = question.Answer, responseTimeMs = 5000 };
        using var graded = await assessment.PostAsJsonAsync($"/attempts/{attempt.GetProperty("id").GetGuid()}/answer", answer, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, graded.StatusCode);
        Assert.True((await graded.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("correct").GetBoolean());
        return attempt.GetProperty("id").GetGuid();
    }

    private async Task<Plan> PlanAsync(string token, CancellationToken cancellationToken)
    {
        using var planning = Client(services.Planning.CreateClient(), token);
        using var response = await planning.GetAsync("/plan/today", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Plan>(JsonSerializerOptions.Web, cancellationToken))!;
    }

    private static int LearnMinutes(Plan plan, Guid topic) =>
        plan.Blocks.Where(block => block.Kind == "learn" && block.TopicId == topic).Sum(block => block.Minutes);

    // Position among today's learn blocks, after all of them when the topic isn't on today.
    private static int FirstLearnBlock(Plan plan, Guid topic)
    {
        var learn = plan.Blocks.Where(block => block.Kind == "learn").ToList();
        var index = learn.FindIndex(block => block.TopicId == topic);
        return index < 0 ? int.MaxValue : index;
    }

    private List<Guid> QuestionTopics(string user) =>
        services.QueryAssessment(db => db.Questions.IgnoreQueryFilters()
            .Where(question => question.OwnerId == user && question.TopicId != null)
            .Select(question => question.TopicId!.Value)
            .Distinct()
            .ToList());

    private static HttpClient Client(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Content's AI on the simulated provider.
    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
        builder.AddContentAi();
        return builder.Build();
    }

    // Consumers, dispatchers and the worker run in the background.
    private static async Task WaitForAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    private sealed record Plan(DateOnly StudyDay, List<PlannedBlock> Blocks, bool MasteryAvailable);

    private sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind);
}