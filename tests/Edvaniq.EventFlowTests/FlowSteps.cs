using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PlanningDomain = Edvaniq.Services.Planning.Domain;
using PlanningInfra = Edvaniq.Services.Planning.Infrastructure;

namespace Edvaniq.EventFlowTests;

// The steps of the learning flow a user takes through the services, shared by PlanFlowTests and CoreFlowTests. Each
// call goes over the service's own HTTP API with the user's token, only the solutions are read from Assessment's
// database.
public sealed class FlowSteps(FlowServices services) : IDisposable
{
    public const string Text = """
        # Photosynthese
        ## Lichtreaktion
        Licht spaltet Wasser.

        ## Calvin-Zyklus
        CO2 wird zu Zucker.

        # Zellatmung
        Zucker wird zu Energie.
        """;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IHost ai = SimulatedAi();

    private IChatClient Chat => ai.Services.GetRequiredService<IChatClient>();

    public void Dispose() => ai.Dispose();

    // The pasted text the whole way: topics in Knowledge, questions in Assessment, and Planning and LearningEngine know
    // every topic of the goal, the default goal without one.
    public async Task<List<Guid>> TopicsWithQuestionsAsync(string token, string user, Guid? learningGoalId = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var content = Client(services.Content.CreateClient(), token);
        object text = learningGoalId is { } goal
            ? new { text = Text, learningGoalId = goal, confirmation = "material-1" }
            : new { text = Text, confirmation = "material-1" };
        using var pasted = await content.PostAsJsonAsync("/documents", text, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
        var documentId = (await pasted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();

        await ActivatorUtilities.CreateInstance<DocumentSplitter>(services.Content.Services).SplitAsync(documentId, cancellationToken);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(services.Content.Services, Chat).AnalyzeAsync(documentId, cancellationToken);
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(services.Content.Services, Chat);
        await WaitForAsync(async () => await pools.FinishAsync(documentId, cancellationToken), "The worker never got the topics from Knowledge.");

        var topics = services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters()
            .Where(topic => topic.DocumentId == documentId).Select(topic => topic.Id).ToList());
        Assert.NotEmpty(topics);
        await FlowServices.WaitForAsync(
            () => Task.FromResult(QuestionTopics(user).Count == topics.Count),
            () => $"Assessment did not take the questions of every topic over. {services.QuestionsOf(documentId, user)}",
            Timeout);
        foreach (var receiver in services.Receivers)
        {
            await WaitForAsync(
                () => Task.FromResult(receiver.KnownTopics(user).Count == topics.Count),
                $"{receiver} did not take the topics over.");
        }

        return topics;
    }

    // Asks a question of the topic and answers it right, with the solution read from Assessment's database.
    public async Task<Guid> AnswerRightAsync(string token, Guid topicId, CancellationToken cancellationToken)
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

    // The check questions of the unit under the session's id, so they count for the introduction (TopicIntroduction),
    // each answered right with the solution read from Assessment's database. Gives the id of the last answer.
    public async Task<Guid> AnswerCheckQuestionsRightAsync(string token, Guid topicId, Guid sessionId, CancellationToken cancellationToken)
    {
        using var assessment = Client(services.Assessment.CreateClient(), token);
        using var asked = await assessment.PostAsJsonAsync("/check-questions", new { topicId, sessionId }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        var check = await asked.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal("ready", check.GetProperty("status").GetString());

        var last = Guid.Empty;
        foreach (var attemptId in check.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("id").GetGuid()))
        {
            var question = services.QueryAssessment(db =>
            {
                var questionId = db.Attempts.IgnoreQueryFilters().Single(attempt => attempt.Id == attemptId).QuestionId;
                return db.Questions.IgnoreQueryFilters().Single(item => item.Id == questionId);
            });
            object answer = question.Type == QuestionType.MultipleChoice
                ? new { options = question.Answers, responseTimeMs = 5000 }
                : new { option = question.Answer, responseTimeMs = 5000 };
            using var graded = await assessment.PostAsJsonAsync($"/attempts/{attemptId}/answer", answer, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, graded.StatusCode);
            Assert.True((await graded.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("correct").GetBoolean());
            last = attemptId;
        }

        Assert.NotEqual(Guid.Empty, last);
        return last;
    }

    public static async Task<Dictionary<Guid, PlanningDomain.TopicStanding>> StandingsAsync(HttpClient learningEngine, Guid goal, CancellationToken cancellationToken)
    {
        using var response = await learningEngine.GetAsync($"/mastery?learningGoalId={goal}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return body.GetProperty("topics").EnumerateArray().ToDictionary(
            topic => topic.GetProperty("topicId").GetGuid(),
            topic => new PlanningDomain.TopicStanding(
                topic.GetProperty("mastery").GetInt32(),
                PlanningInfra.LearningEngineMastery.StateOf(topic.GetProperty("state").GetString())));
    }

    public static async Task<PlanningDomain.DailyBudget> BudgetAsync(HttpClient planning, CancellationToken cancellationToken)
    {
        var week = await planning.GetFromJsonAsync<JsonElement>("/daily-budget", cancellationToken);
        return PlanningDomain.DailyBudget.Of(Enum.GetValues<DayOfWeek>().ToDictionary(
            day => day, day => week.GetProperty(day.ToString().ToLowerInvariant()).GetInt32()));
    }

    public async Task<Plan> PlanAsync(string token, CancellationToken cancellationToken)
    {
        using var planning = Client(services.Planning.CreateClient(), token);
        using var response = await planning.GetAsync("/plan/today", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Plan>(JsonSerializerOptions.Web, cancellationToken))!;
    }

    public static async Task<Plan> PlanWithIdsAsync(HttpClient planning, CancellationToken cancellationToken)
    {
        using var response = await planning.GetAsync("/plan/today", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<Plan>(JsonSerializerOptions.Web, cancellationToken))!;
        Assert.True(plan.MasteryAvailable);
        return plan;
    }

    public static async Task<JsonElement> SendAsync(HttpClient planning, string path, object body, HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using var response = await planning.PostAsJsonAsync(path, body, cancellationToken);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    public static async Task<Guid?> LastAnswerAsync(HttpClient learningEngine, Guid topic, CancellationToken cancellationToken)
    {
        using var response = await learningEngine.GetAsync($"/mastery/{topic}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var standing = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return standing.GetProperty("lastAnswerId") is { ValueKind: JsonValueKind.String } id ? id.GetGuid() : null;
    }

    // Today's plan as the planner makes it of these standings, with the blocks done so far kept (#375). The topics with
    // their order, sizes and foundations come from Planning's own queries, so the comparison follows whatever the planner
    // takes in (#380 added sizes and ranks).
    public async Task<ReplanCheck> ReplanAsync(
        HttpClient planning,
        string user,
        Guid goal,
        Plan before,
        Plan after,
        Guid topic,
        IReadOnlyDictionary<Guid, PlanningDomain.TopicStanding> standingsBefore,
        IReadOnlyDictionary<Guid, PlanningDomain.TopicStanding> standingsAfter,
        CancellationToken cancellationToken)
    {
        var budget = await BudgetAsync(planning, cancellationToken);
        IReadOnlyList<Guid> known;
        IReadOnlyDictionary<Guid, int> sizes;
        IReadOnlySet<Guid> foundations;
        using (var scope = services.Planning.Services.CreateScope())
        {
            var topics = scope.ServiceProvider.GetRequiredService<IKnownTopics>();
            known = await topics.ActiveAsync(user, goal, cancellationToken);
            sizes = await topics.SizesAsync(user, goal, cancellationToken);
            foundations = await topics.FoundationsAsync(user, goal, cancellationToken);
        }

        var done = services.QueryPlanning(db => db.DoneBlocks.IgnoreQueryFilters().Where(entry => entry.OwnerId == user).ToList());
        var withOld = new Dictionary<Guid, PlanningDomain.TopicStanding>(standingsAfter) { [topic] = standingsBefore[topic] };
        var ready = known.Except(after.NotReadyTopicIds ?? []).ToHashSet();
        IReadOnlyList<PlanningDomain.Block> Fresh(IReadOnlyDictionary<Guid, PlanningDomain.TopicStanding> standings) =>
            PlanningDomain.TodayPlanner.Plan(known, standings, budget, DateTime.UtcNow, ready, foundations, sizes).Blocks;
        var fresh = Fresh(standingsAfter);
        var expected = PlanningDomain.DayBlocks.WithDone(user, goal, before.StudyDay, fresh, done, budget.MinutesOn(before.StudyDay))
            .Select(entry => (entry.Id, entry.Block.TopicId, entry.Block.Minutes, Kind: entry.Block.Kind.ToString().ToLowerInvariant(), entry.Done))
            .ToList();
        int LearnOf(IReadOnlyList<PlanningDomain.Block> blocks) =>
            blocks.Where(entry => entry.TopicId == topic && entry.Kind == PlanningDomain.BlockKind.Learn).Sum(entry => entry.Minutes);
        return new ReplanCheck(expected, LearnOf(Fresh(withOld)), LearnOf(fresh));
    }

    public List<Guid> QuestionTopics(string user) =>
        services.QueryAssessment(db => db.Questions.IgnoreQueryFilters()
            .Where(question => question.OwnerId == user && question.TopicId != null)
            .Select(question => question.TopicId!.Value)
            .Distinct()
            .ToList());

    public static HttpClient Client(HttpClient client, string token)
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
    public static async Task WaitForAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    public sealed record Plan(
        DateOnly StudyDay, List<PlannedBlock> Blocks, bool MasteryAvailable, List<Guid>? NotReadyTopicIds = null, Guid LearningGoalId = default);

    public sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind, Guid Id = default, bool Done = false);

    // The plan the planner makes of the new standings, and the learn minutes of the topic in a fresh plan with its old and
    // its new mastery.
    public sealed record ReplanCheck(
        IReadOnlyList<(Guid Id, Guid TopicId, int Minutes, string Kind, bool Done)> Expected, int LearnWithOld, int LearnWithNew)
    {
        public bool Follows(Plan plan) =>
            Expected.SequenceEqual(plan.Blocks.Select(entry => (entry.Id, entry.TopicId, entry.Minutes, entry.Kind, entry.Done)));
    }
}