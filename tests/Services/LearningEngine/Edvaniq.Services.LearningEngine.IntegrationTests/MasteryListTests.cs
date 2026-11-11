using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// GET /mastery: the mastery of all of the user's topics or those of one learning goal, for Planning and the client.
public sealed class MasteryListTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    // Records a minute old: one correct answer gives 62, one wrong 37 (MasteryTests).
    private static readonly DateTime AMinuteAgo = DateTime.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task ListsEveryTopic_ThoseNeverPractisedWithTheStartValue()
    {
        var alice = NewUser("alice");
        var (up, down, untouched) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(
            [Topic(alice, up), Topic(alice, down), Topic(alice, untouched, Guid.NewGuid())],
            [Record(alice, up, correct: true), Record(alice, down, correct: false)]);

        var list = await ListAsync(alice);

        Assert.Equal(Mastery.RuleVersion, list.RuleVersion);
        Assert.Equal(
            new[] { new Entry(up, 62), new Entry(down, 37), new Entry(untouched, 50) }.OrderBy(entry => entry.TopicId),
            list.Topics);
    }

    [Fact]
    public async Task WithALearningGoal_ListsOnlyItsTopics()
    {
        var alice = NewUser("alice");
        var (biology, history) = (Guid.NewGuid(), Guid.NewGuid());
        var (cell, rome) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync([Topic(alice, cell, biology), Topic(alice, rome, history)], [Record(alice, cell, correct: true)]);

        Assert.Equal(new[] { new Entry(cell, 62) }, (await ListAsync(alice, $"?learningGoalId={biology}")).Topics);
        Assert.Equal(new[] { new Entry(rome, 50) }, (await ListAsync(alice, $"?learningGoalId={history}")).Topics);
        Assert.Empty((await ListAsync(alice, $"?learningGoalId={Guid.NewGuid()}")).Topics);
    }

    // Bob answered on Alice's topic id and asks with her learning goal: he gets nothing of hers, she nothing of his.
    [Fact]
    public async Task OnlyTheUsersOwnTopicsAndAnswers()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var (goal, topic) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(
            [Topic(alice, topic, goal)],
            [Record(alice, topic, correct: true), Record(bob, topic, correct: false), Record(bob, topic, correct: false)]);

        Assert.Equal(new[] { new Entry(topic, 62) }, (await ListAsync(alice)).Topics);
        Assert.Empty((await ListAsync(bob)).Topics);
        Assert.Empty((await ListAsync(bob, $"?learningGoalId={goal}")).Topics);
    }

    [Fact]
    public async Task BrokenLearningGoalId_IsAProblem()
    {
        using var response = await GetAsync(NewUser("alice"), "?learningGoalId=not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty("learningGoalId", out _));
    }

    [Fact]
    public async Task Request_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.GetAsync("/mastery", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Q1: under 500 ms with the amount of data of the walking skeleton, 50 topics (Q1) with 10 answers each. The median
    // of five calls after one to warm up, so a single hiccup of the runner doesn't decide it.
    [Fact]
    public async Task FiftyTopicsWithTenAnswersEach_AnswerInUnder500Ms()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var topics = Enumerable.Range(0, 50).Select(_ => Topic(alice, Guid.NewGuid(), goal)).ToList();
        var records = topics
            .SelectMany(topic => Enumerable.Range(0, 10).Select(i => Record(alice, topic.TopicId, correct: i % 3 != 0)))
            .ToList();
        await SeedAsync(topics, records);

        Assert.Equal(50, (await ListAsync(alice)).Topics.Count);
        var times = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            var list = await ListAsync(alice);
            times.Add(watch.Elapsed.TotalMilliseconds);
            Assert.Equal(50, list.Topics.Count);
        }

        var median = times.Order().ElementAt(2);
        TestContext.Current.SendDiagnosticMessage(
            $"GET /mastery, 50 topics, 500 records: median {median:F1} ms ({string.Join(", ", times.Select(t => $"{t:F1}"))})");
        Assert.True(median < 500, $"Median {median:F1} ms, limit 500 ms.");
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static KnownTopic Topic(string owner, Guid topicId, Guid? learningGoalId = null) =>
        KnownTopic.Create(owner, topicId, learningGoalId ?? Guid.NewGuid(), hidden: false);

    private static LearningRecord Record(string owner, Guid topicId, bool correct) =>
        LearningRecord.Create(
            Guid.NewGuid(), owner, topicId, Guid.NewGuid(), null, correct, 2, 4200, null, Mode.Practice, AMinuteAgo);

    // Topics only come from TopicsChanged and records from AnswerRecorded, KnownTopicTests and MasteryTests cover
    // those ways. Here both go into the database directly.
    private async Task SeedAsync(IEnumerable<KnownTopic> topics, IEnumerable<LearningRecord> records)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.AddRange(topics);
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<MasteryList> ListAsync(string user, string query = "")
    {
        using var response = await GetAsync(user, query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MasteryList>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string query)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mastery" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record MasteryList(int RuleVersion, IReadOnlyList<Entry> Topics);

    private sealed record Entry(Guid TopicId, int Mastery);
}