using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using LearningEngineInfra = Edvaniq.Services.LearningEngine.Infrastructure;
using TopicIntroduction = Edvaniq.Services.LearningEngine.Domain.TopicIntroduction;

namespace Edvaniq.EventFlowTests;

// #386: a topic is new until its unit "Neues Thema", afterwards its mastery comes from the unit's check questions and
// the plan stops treating it as new. Check questions in Assessment with the session of the unit, SessionCompleted sent
// by hand like Planning will from #395, LearningEngine's mastery and Planning's plan for today.
[Collection(FlowCollection.Name)]
public sealed class IntroductionFlowTests(FlowServices services) : IDisposable
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
    public async Task AfterTheUnit_TheTopicIsIntroducedOnce_WithTheMasteryOfItsCheckQuestions()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(alice);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = (await TopicsWithQuestionsAsync(token, alice))[0];

        // Before the unit: new at the start value, and with every topic new the plan has no review.
        Assert.Equal((50, "new"), await MasteryAsync(token, topic, cancellationToken));
        Assert.DoesNotContain(await BlocksAsync(token, cancellationToken), block => block.Kind == "review");

        // The unit: its check questions, all wrong, then the end of the session.
        var unit = Guid.NewGuid();
        var answers = await AnswerCheckQuestionsWrongAsync(token, topic, unit, cancellationToken);
        await WaitForAsync(() => Task.FromResult(Records(alice, topic) == answers), "LearningEngine did not store the check answers.");
        var completed = new SessionCompleted(alice, unit, DateTime.UtcNow, true, [topic]);
        await PublishAsync(completed, cancellationToken);
        await WaitForAsync(() => Task.FromResult(Introductions(alice).Count == 1), "LearningEngine did not introduce the topic.");

        // After: the check questions count, though answered before the session ended. Wrong ones make it a weakness.
        var (mastery, state) = await MasteryAsync(token, topic, cancellationToken);
        Assert.True(mastery < 50, $"Mastery {mastery} did not come from the wrong check answers.");
        Assert.Equal("weakness", state);

        // The same unit finished again, as the same session under a new message id, and a later session naming the
        // topic too: still one introduction, the first, and the check answers count once.
        var again = await PublishAsync(completed, cancellationToken);
        var later = await PublishAsync(completed with { SessionId = Guid.NewGuid(), EndedAt = DateTime.UtcNow }, cancellationToken);
        await WaitForAsync(() => Task.FromResult(Handled(again) && Handled(later)), "LearningEngine did not handle the repeated sessions.");
        Assert.Equal(unit, Assert.Single(Introductions(alice)).SessionId);
        Assert.Equal((mastery, state), await MasteryAsync(token, topic, cancellationToken));

        // The plan: the topic is no longer new, so today starts with its review.
        Assert.Equal(("review", topic), (await BlocksAsync(token, cancellationToken)).Select(block => (block.Kind, block.TopicId)).First());
    }

    public void Dispose() => ai.Dispose();

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
            () => Task.FromResult(services.QueryAssessment(db => db.Questions.IgnoreQueryFilters()
                .Where(question => question.OwnerId == user && question.TopicId != null)
                .Select(question => question.TopicId)
                .Distinct()
                .Count()) == topics.Count),
            "Assessment did not take the questions of every topic over.");
        foreach (var receiver in services.Receivers)
        {
            await WaitForAsync(
                () => Task.FromResult(receiver.KnownTopics(user).Count == topics.Count),
                $"{receiver} did not take the topics over.");
        }

        return topics;
    }

    // Shows the solution of every check question, which grades it wrong whatever kind of question it is.
    private async Task<int> AnswerCheckQuestionsWrongAsync(string token, Guid topicId, Guid sessionId, CancellationToken cancellationToken)
    {
        using var assessment = Client(services.Assessment.CreateClient(), token);
        using var asked = await assessment.PostAsJsonAsync("/check-questions", new { topicId, sessionId }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        var check = await asked.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("ready", check.GetProperty("status").GetString());

        var ids = check.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("id").GetGuid()).ToList();
        foreach (var id in ids)
        {
            using var graded = await assessment.PostAsJsonAsync($"/attempts/{id}/answer", new { showSolution = true, responseTimeMs = 5000 }, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, graded.StatusCode);
        }

        return ids.Count;
    }

    // Into Planning's stream, like its dispatcher would.
    private async Task<Guid> PublishAsync(SessionCompleted completed, CancellationToken cancellationToken)
    {
        await services.Broker.EnsureStreamAsync(
            new StreamSpec(EventRoute.StreamOf("planning"), EventRoute.SubjectsOf("planning")), cancellationToken);
        var messageId = Guid.NewGuid();
        await services.Broker.PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(SessionCompleted)).Subject, messageId, JsonSerializer.Serialize(completed, JsonSerializerOptions.Web)),
            cancellationToken);
        return messageId;
    }

    private async Task<(int Mastery, string State)> MasteryAsync(string token, Guid topic, CancellationToken cancellationToken)
    {
        using var learningEngine = Client(services.LearningEngine.CreateClient(), token);
        using var response = await learningEngine.GetAsync($"/mastery/{topic}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return (body.GetProperty("mastery").GetInt32(), body.GetProperty("state").GetString()!);
    }

    private async Task<List<PlannedBlock>> BlocksAsync(string token, CancellationToken cancellationToken)
    {
        using var planning = Client(services.Planning.CreateClient(), token);
        using var response = await planning.GetAsync("/plan/today", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<Plan>(JsonSerializerOptions.Web, cancellationToken))!;
        Assert.True(plan.MasteryAvailable);
        return plan.Blocks;
    }

    private int Records(string user, Guid topic) =>
        services.QueryLearningEngine(db => db.LearningRecords.IgnoreQueryFilters().Count(record => record.OwnerId == user && record.TopicId == topic));

    private List<TopicIntroduction> Introductions(string user) =>
        services.QueryLearningEngine(db => db.TopicIntroductions.IgnoreQueryFilters().Where(item => item.OwnerId == user).ToList());

    private bool Handled(Guid messageId) =>
        services.QueryLearningEngine(db => db.Set<InboxMessage>().Any(entry =>
            entry.MessageId == messageId && entry.Consumer == $"{LearningEngineInfra.InfrastructureExtensions.ServiceKey}-SessionCompletedHandler"));

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