using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using static Edvaniq.Services.Planning.IntegrationTests.AssessmentStub;
using static Edvaniq.Services.Planning.IntegrationTests.LearningEngineStub;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #403: GET /sessions/{id}/result, the answers from Assessment, the mastery before and after from LearningEngine, the
// daily goal and the next step from today's plan.
public sealed class SessionResultTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly StubStanding Before = new(50, "building");
    private static readonly StubStanding After = new(62, "building");

    // AC1 to AC3: the counts are Assessment's answers, the mastery LearningEngine's before and after, and the next step
    // the next open block.
    [Fact]
    public async Task Completed_GivesCounts_MasteryChange_AndTheNextBlock()
    {
        var (token, topics) = Seed(2);
        var plan = await PlanAsync(token);
        Assert.True(plan.Blocks.Count >= 2);
        var block = plan.Blocks[0];
        var session = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{session}/complete");
        service.Assessment.SetAnswers(token, block.Id, Answer(block.TopicId, true), Answer(block.TopicId, false), Answer(block.TopicId, true));
        foreach (var topic in topics)
        {
            service.LearningEngine.SetChange(token, topic, Before, After);
        }

        var (status, result) = await ResultAsync(token, session);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((session, "completed"), (result!.SessionId, result.Outcome));
        Assert.Equal((3, 2, 1), (result.Answered, result.Correct, result.Wrong));
        Assert.True(result.Complete);
        Assert.True(result.MasteryAvailable);
        Assert.Equal([new Change(block.TopicId, new(50, "building"), new(62, "building"))], result.Topics);
        Assert.Equal("open", result.DailyGoal);
        Assert.Equal(new NextStep("block", plan.Blocks[1].Id, plan.Blocks[1].TopicId, plan.Blocks[1].Kind, plan.Blocks[1].Minutes), result.NextStep);
    }

    // AC3: nothing left open, the goal is reached and the next step is a bonus review.
    [Fact]
    public async Task LastBlock_GivesABonusReview()
    {
        var (token, _) = Seed(1);
        var plan = await PlanAsync(token);
        string? goal = null;
        Result? result = null;
        foreach (var block in plan.Blocks)
        {
            var session = await StartAsync(token, block.Id);
            await PostAsync(token, $"/sessions/{session}/complete");
            (_, result) = await ResultAsync(token, session);
            goal = result!.DailyGoal;
        }

        Assert.Equal("reached", goal);
        Assert.Equal(new NextStep("bonusReview", null, null, null, null), result!.NextStep);
    }

    // AC4: a block broken off has a result of what was answered, and after it was started again and finished, the result
    // counts both sessions: Assessment is asked for the block with both, LearningEngine from the start of the first.
    [Fact]
    public async Task BrokenOff_AndStartedAgain_CountsBothSessions()
    {
        var (token, topics) = Seed(1);
        var block = (await PlanAsync(token)).Blocks[0];
        service.LearningEngine.SetChange(token, topics[0], Before, After);
        var first = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{first}/abort");
        service.Assessment.SetAnswers(token, block.Id, Answer(block.TopicId, true), Answer(block.TopicId, false));

        var (_, partial) = await ResultAsync(token, first);

        var second = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{second}/complete");
        service.Assessment.SetAnswers(
            token, block.Id, Answer(block.TopicId, true), Answer(block.TopicId, false), Answer(block.TopicId, true), Answer(block.TopicId, true));
        var (_, whole) = await ResultAsync(token, second);

        Assert.Equal(("aborted", 2, 1), (partial!.Outcome, partial.Answered, partial.Correct));
        Assert.Equal(("completed", 4, 3), (whole!.Outcome, whole.Answered, whole.Correct));
        var asked = service.Assessment.RequestsWith(token).Select(request => request.PathAndQuery).Where(path => path.StartsWith("/answers", StringComparison.Ordinal)).Last();
        Assert.Contains($"sessionId={first}", asked, StringComparison.Ordinal);
        Assert.Contains($"sessionId={second}", asked, StringComparison.Ordinal);
        var started = Query(db => db.Sessions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Single(session => session.Id == first).StartedAt);
        var changes = service.LearningEngine.RequestsWith(token).Select(request => request.PathAndQuery).Last(path => path.StartsWith("/mastery/changes", StringComparison.Ordinal));
        Assert.Contains($"from={Uri.EscapeDataString(started.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}", changes, StringComparison.Ordinal);
    }

    // #406 in advance: while LearningEngine misses an answer, or can't be asked, the counts come without any mastery.
    [Fact]
    public async Task AnswersNotArrived_OrLearningEngineDown_GiveNoMastery()
    {
        var (token, topics) = Seed(1);
        var block = (await PlanAsync(token)).Blocks[0];
        var session = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{session}/complete");
        service.Assessment.SetAnswers(token, block.Id, Answer(block.TopicId, true));
        service.LearningEngine.SetChange(token, topics[0], Before, After);
        service.LearningEngine.SetMissing(token, 1);

        var (_, pending) = await ResultAsync(token, session);
        service.LearningEngine.Unreachable(token);
        var (status, down) = await ResultAsync(token, session);

        Assert.Equal((1, false, true, null), (pending!.Answered, pending.Complete, pending.MasteryAvailable, pending.Topics));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((1, false, false, null), (down!.Answered, down.Complete, down.MasteryAvailable, down.Topics));
        var entry = Assert.Single(service.Logs.Entries, entry => entry.EventName == "SessionResultWithoutMastery" && entry.Message.Contains(session.ToString(), StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(service.Logs.Entries, entry => entry.EventName == "MasteryChangesUnavailable");
    }

    // #406 AC1 and AC3 with a late delivery: while LearningEngine misses an answer the result has no mastery. Once the
    // answer has arrived the result is complete, and the plan asked after it asks LearningEngine afresh, so it is made
    // from the new mastery. Nothing in Planning keeps the old one.
    [Fact]
    public async Task LateDelivery_ResultWaits_AndThePlanAfterItUsesTheNewMastery()
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        var topic = Guid.NewGuid();
        AddKnownTopic(user, topic);
        service.Assessment.Set(token, topic);
        service.LearningEngine.Set(token, topic, 50, "new");
        var block = (await PlanAsync(token)).Blocks.Single();
        Assert.Equal("learn", block.Kind);
        var session = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{session}/complete");
        service.Assessment.SetAnswers(token, block.Id, Answer(topic, true), Answer(topic, true));
        service.LearningEngine.SetChange(token, topic, new StubStanding(50, "new"), new StubStanding(50, "new"));
        service.LearningEngine.SetMissing(token, 2);

        var (_, pending) = await ResultAsync(token, session);

        // LearningEngine has the answers and the introduction now.
        service.LearningEngine.SetMissing(token, 0);
        service.LearningEngine.SetChange(token, topic, new StubStanding(50, "new"), new StubStanding(72, "building"), introduced: true);
        service.LearningEngine.Set(token, topic, 72, "building");
        var (_, complete) = await ResultAsync(token, session);
        var asked = service.LearningEngine.RequestsWith(token).Count;
        var planAfter = await PlanAsync(token);

        Assert.Equal((2, false, null), (pending!.Answered, pending.Complete, pending.Topics));
        Assert.True(complete!.Complete);
        Assert.Equal([new Change(topic, new(50, "new"), new(72, "building"))], complete.Topics);
        Assert.Contains(
            service.LearningEngine.RequestsWith(token).Skip(asked),
            request => request.PathAndQuery.StartsWith("/mastery?", StringComparison.Ordinal));
        Assert.True(planAfter.Blocks.Single(entry => entry.Id == block.Id).Done);
    }

    // #406: a topic gone from the goal since (#389) gives the counts and topicRemoved, never a mastery or an endless wait.
    // A hidden topic is still known and gives a normal result.
    [Fact]
    public async Task RemovedTopic_IsNamed_HiddenTopicGivesANormalResult()
    {
        var (token, topics) = Seed(1);
        var user = Query(db => db.KnownTopics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Single(topic => topic.TopicId == topics[0]).OwnerId);
        var block = (await PlanAsync(token)).Blocks[0];
        var session = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{session}/complete");
        service.Assessment.SetAnswers(token, block.Id, Answer(block.TopicId, true));
        service.LearningEngine.SetChange(token, block.TopicId, Before, After);

        ChangeKnownTopic(user, block.TopicId, topic => topic.Update(topic.LearningGoalId, hidden: true));
        var (hiddenStatus, hidden) = await ResultAsync(token, session);
        ChangeKnownTopic(user, block.TopicId, topic => topic.Update(topic.LearningGoalId, hidden: false), remove: true);
        var (removedStatus, removed) = await ResultAsync(token, session);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (hiddenStatus, removedStatus));
        Assert.Equal((false, true, 1), (hidden!.TopicRemoved, hidden.Complete, hidden.Answered));
        Assert.NotNull(hidden.Topics);
        Assert.Equal((true, false, 1), (removed!.TopicRemoved, removed.Complete, removed.Answered));
        Assert.Null(removed.Topics);
    }

    [Fact]
    public async Task AssessmentDown_IsServiceUnavailable()
    {
        var (token, _) = Seed(1);
        var block = (await PlanAsync(token)).Blocks[0];
        var session = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{session}/complete");
        service.Assessment.Unreachable(token);

        var (status, _) = await ResultAsync(token, session);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        var entry = Assert.Single(service.Logs.Entries, entry => entry.EventName == "SessionResultWithoutAnswers" && entry.Message.Contains(session.ToString(), StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public async Task RunningSession_IsAConflict_AndAnUnknownOneNotFound()
    {
        var (token, _) = Seed(1);
        var session = await StartAsync(token, (await PlanAsync(token)).Blocks[0].Id);

        var (running, _) = await ResultAsync(token, session);
        var (unknown, _) = await ResultAsync(token, Guid.NewGuid());

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.NotFound), (running, unknown));
    }

    private static StubAnswer Answer(Guid topic, bool correct) => new(Guid.NewGuid(), topic, correct, DateTime.UtcNow);

    // A user with this many topics in building, each with a ready pool.
    private (string Token, List<Guid> Topics) Seed(int count)
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        var topics = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        using (var scope = service.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
            foreach (var topic in topics)
            {
                db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
            }

            db.SaveChanges();
        }

        foreach (var (topic, index) in topics.Select((topic, index) => (topic, index)))
        {
            service.Assessment.Set(token, topic);
            service.LearningEngine.Set(token, topic, 20 + (40 * index), "building");
        }

        return (token, topics);
    }

    private void AddKnownTopic(string user, Guid topic)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
        db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
        db.SaveChanges();
    }

    // As TopicsChanged would: change the topic, or take it out of the goal.
    private void ChangeKnownTopic(string user, Guid topic, Action<KnownTopic> change, bool remove = false)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
        var known = db.KnownTopics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Single(entry => entry.OwnerId == user && entry.TopicId == topic);
        change(known);
        if (remove)
        {
            db.KnownTopics.Remove(known);
        }

        db.SaveChanges();
    }

    private T Query<T>(Func<PlanningDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    private HttpClient Client(string token)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Plan> PlanAsync(string token)
    {
        using var client = Client(token);
        return (await client.GetFromJsonAsync<Plan>("/plan/today", TestContext.Current.CancellationToken))!;
    }

    private async Task<Guid> StartAsync(string token, Guid blockId)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Started>(TestContext.Current.CancellationToken))!.Id;
    }

    private async Task PostAsync(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(HttpStatusCode Status, Result? Result)> ResultAsync(string token, Guid session)
    {
        using var client = Client(token);
        using var response = await client.GetAsync($"/sessions/{session}/result", TestContext.Current.CancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Result>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private sealed record Plan(List<PlannedBlock> Blocks, Guid? NextBlockId, string DailyGoal);

    private sealed record PlannedBlock(Guid Id, Guid TopicId, string Kind, int Minutes, bool Done);

    private sealed record Started(Guid Id);

    private sealed record Result(
        Guid SessionId,
        string Outcome,
        int Answered,
        int Correct,
        int Wrong,
        bool Complete,
        bool MasteryAvailable,
        List<Change>? Topics,
        string DailyGoal,
        NextStep NextStep,
        bool TopicRemoved = false);

    private sealed record Change(Guid TopicId, Standing? Before, Standing? After);

    private sealed record Standing(int Mastery, string? State);

    private sealed record NextStep(string Kind, Guid? BlockId, Guid? TopicId, string? BlockKind, int? Minutes);
}