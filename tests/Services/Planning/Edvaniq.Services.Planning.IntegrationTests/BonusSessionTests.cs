using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Edvaniq.Services.Planning.IntegrationTests.AssessmentStub;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #408: POST /sessions/bonus starts a bonus review on the weakest topic once the plan is done. It never counts for the
// daily goal, and without a fitting topic the client gets a sentence instead of an error.
public sealed class BonusSessionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string NoTopic = "Für eine Bonus-Wiederholung gibt es gerade kein geübtes Thema mit Fragen. Lern zuerst ein Thema aus deinem Plan.";

    // A62, AC1 and AC2: the plan is done, the bonus review asks about the weakness with the lowest mastery, and
    // finishing it leaves the done blocks and the daily goal as they were.
    [Fact]
    public async Task DonePlan_BonusReview_OnTheWeakestTopic_LeavesTheDailyGoal()
    {
        var (user, token) = NewUser();
        var weakest = Guid.NewGuid();
        AddTopic(user, token, Guid.NewGuid(), "weakness", 35);
        AddTopic(user, token, weakest, "weakness", 25);
        AddTopic(user, token, Guid.NewGuid(), "building", 10);
        var done = await FinishThePlanAsync(token);
        Assert.Equal(("reached", (Guid?)null), (done.DailyGoal, done.NextBlockId));
        var doneBlocks = DoneBlocksOf(user);

        var (status, bonus) = await StartBonusAsync(token);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(bonus!.Bonus);
        Assert.Equal((weakest, "review", BonusReview.Minutes, done.StudyDay), (bonus.TopicId, bonus.Kind, bonus.Minutes, bonus.StudyDay));
        Assert.Equal(["review", "quiz"], bonus.Steps.Select(step => step.Kind));
        Assert.DoesNotContain(done.Blocks, block => block.Id == bonus.BlockId);
        Assert.Equal((Guid?)null, (await PlanAsync(token)).NextBlockId);

        var (completeStatus, completed) = await PostAsync(token, $"/sessions/{bonus.Id}/complete");
        var after = await PlanAsync(token);

        Assert.Equal((HttpStatusCode.OK, "completed"), (completeStatus, completed!.Outcome));
        Assert.Equal(doneBlocks, DoneBlocksOf(user));
        Assert.Equal(done.Blocks, after.Blocks);
        Assert.Equal(("reached", (Guid?)null), (after.DailyGoal, after.NextBlockId));

        // It ends like any session, it only introduces nothing.
        var sent = Assert.Single(EventsOf(bonus.Id));
        Assert.True(sent.Completed);
        Assert.Empty(sent.IntroducedTopicIds);
        Assert.Equal(bonus.StartedAt, sent.UnitStartedAt);

        // #417: in the log with its ids, never with the user.
        Assert.Single(service.Logs.Entries, entry => entry.EventName == "BonusReviewStarted" && entry.Message.Contains(bonus.Id.ToString(), StringComparison.Ordinal));
        Assert.Empty(service.Logs.Containing(user));
    }

    // Started again, it is the running one, after its end a new one with a block id of its own.
    [Fact]
    public async Task SecondStart_BringsBackTheRunningBonusReview_AfterItsEndANewOne()
    {
        var (user, token) = NewUser();
        AddTopic(user, token, Guid.NewGuid(), "weakness", 30);
        var (_, first) = await StartBonusAsync(token);

        var (againStatus, again) = await StartBonusAsync(token);
        await PostAsync(token, $"/sessions/{first!.Id}/abort");
        var (nextStatus, next) = await StartBonusAsync(token);

        Assert.Equal(HttpStatusCode.OK, againStatus);
        Assert.Equal((first.Id, true), (again!.Id, again.Existing));
        Assert.Equal(HttpStatusCode.Created, nextStatus);
        Assert.NotEqual(first.BlockId, next!.BlockId);
        Assert.Empty(EventsOf(first.Id).SelectMany(sent => sent.IntroducedTopicIds));
    }

    // A block session running is the one the user gets back, no bonus review beside it.
    [Fact]
    public async Task RunningBlockSession_IsBroughtBack()
    {
        var (user, token) = NewUser();
        AddTopic(user, token, Guid.NewGuid(), "weakness", 30);
        var block = (await PlanAsync(token)).Blocks[0];
        var (_, running) = await PostAsync(token, "/sessions", new { blockId = block.Id });

        var (status, bonus) = await StartBonusAsync(token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((running!.Id, false), (bonus!.Id, bonus.Bonus));
    }

    // Without a weakness the introduced topic with the lowest mastery. New topics and those without questions never.
    [Fact]
    public async Task WithoutWeakness_TheWeakestIntroducedTopicWithQuestions()
    {
        var (user, token) = NewUser();
        var expected = Guid.NewGuid();
        AddTopic(user, token, Guid.NewGuid(), "new", 0);
        AddTopic(user, token, Guid.NewGuid(), "building", 20, pool: "pending");
        AddTopic(user, token, expected, "building", 40);
        AddTopic(user, token, Guid.NewGuid(), "secure", 90);

        var (status, bonus) = await StartBonusAsync(token);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(expected, bonus!.TopicId);
    }

    // AC3: no topics, or only new ones and ones without questions: a sentence for the client, no session.
    [Fact]
    public async Task NoFittingTopic_IsAConflictWithASentence()
    {
        var (_, empty) = NewUser();
        var (user, token) = NewUser();
        AddTopic(user, token, Guid.NewGuid(), "new", 0);
        AddTopic(user, token, Guid.NewGuid(), "weakness", 20, pool: "pending");

        foreach (var caller in new[] { empty, token })
        {
            var (status, title) = await ProblemAsync(caller);

            Assert.Equal((HttpStatusCode.Conflict, NoTopic), (status, title));
            Assert.Equal(HttpStatusCode.NoContent, (await GetAsync(caller, "/sessions/active")).Status);
        }

        Assert.Contains(service.Logs.Entries, entry => entry.EventName == "BonusReviewWithoutTopic");
    }

    // Without LearningEngine the weakest topic isn't known: try again, not "learn something first".
    [Fact]
    public async Task LearningEngineDown_IsUnavailable()
    {
        var (user, token) = NewUser();
        AddTopic(user, token, Guid.NewGuid(), "weakness", 30);
        service.LearningEngine.Unreachable(token);

        var (status, title) = await ProblemAsync(token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("Die Bonus-Wiederholung lässt sich gerade nicht zusammenstellen. Versuch es gleich noch einmal.", title);
        Assert.Equal(HttpStatusCode.NoContent, (await GetAsync(token, "/sessions/active")).Status);
    }

    // Without Assessment every pool counts as ready, as in the plan. Whether the quiz finds questions is up to Assessment
    // when the client asks for it.
    [Fact]
    public async Task AssessmentDown_StillStartsOnTheWeakestTopic()
    {
        var (user, token) = NewUser();
        var weakest = Guid.NewGuid();
        AddTopic(user, token, weakest, "weakness", 15);
        AddTopic(user, token, Guid.NewGuid(), "weakness", 45);
        service.Assessment.Unreachable(token);

        var (status, bonus) = await StartBonusAsync(token);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(weakest, bonus!.TopicId);
    }

    // The quiz of a bonus review is Assessment's block quiz under the bonus block id: the result asks for the answers
    // with it and names the review as one.
    [Fact]
    public async Task Result_OfABonusReview_CountsTheAnswersOfItsBlockId()
    {
        var (user, token) = NewUser();
        var topic = Guid.NewGuid();
        AddTopic(user, token, topic, "weakness", 30);
        var (_, bonus) = await StartBonusAsync(token);
        service.Assessment.SetAnswers(
            token, bonus!.BlockId, new StubAnswer(Guid.NewGuid(), topic, true, DateTime.UtcNow), new StubAnswer(Guid.NewGuid(), topic, false, DateTime.UtcNow));
        await PostAsync(token, $"/sessions/{bonus.Id}/complete");

        using var client = Client(token);
        var result = await client.GetFromJsonAsync<Result>($"/sessions/{bonus.Id}/result", TestContext.Current.CancellationToken);

        Assert.True(result!.Bonus);
        Assert.Equal((2, 1, 1), (result.Answered, result.Correct, result.Wrong));
        Assert.Contains(service.Assessment.RequestsWith(token), request => request.PathAndQuery.Contains($"blockId={bonus.BlockId}", StringComparison.Ordinal));
    }

    // Each user gets a review of their own topics, and one user's running review doesn't stop another's.
    [Fact]
    public async Task EachUser_GetsABonusReviewOnTheirOwnTopics()
    {
        var (alice, aliceToken) = NewUser();
        var (bob, bobToken) = NewUser();
        var alices = Guid.NewGuid();
        var bobs = Guid.NewGuid();
        AddTopic(alice, aliceToken, alices, "weakness", 10);
        AddTopic(bob, bobToken, bobs, "weakness", 50);

        var (_, aliceBonus) = await StartBonusAsync(aliceToken);
        var (bobStatus, bobBonus) = await StartBonusAsync(bobToken);

        Assert.Equal(HttpStatusCode.Created, bobStatus);
        Assert.Equal((alices, bobs), (aliceBonus!.TopicId, bobBonus!.TopicId));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(bobToken, $"/sessions/{aliceBonus.Id}")).Status);
    }

    private async Task<Plan> FinishThePlanAsync(string token)
    {
        foreach (var block in (await PlanAsync(token)).Blocks)
        {
            var (_, session) = await PostAsync(token, "/sessions", new { blockId = block.Id });
            await PostAsync(token, $"/sessions/{session!.Id}/complete");
        }

        return await PlanAsync(token);
    }

    private static (string User, string Token) NewUser()
    {
        var user = $"alice-{Guid.NewGuid():N}";
        return (user, TestTokens.Create(user));
    }

    private void AddTopic(string user, string token, Guid topic, string state, int mastery, string pool = "ready")
    {
        using (var scope = service.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
            db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
            db.SaveChanges();
        }

        service.Assessment.Set(token, topic, pool);
        service.LearningEngine.Set(token, topic, mastery, state);
    }

    private List<Guid> DoneBlocksOf(string user) =>
        Query(db => db.DoneBlocks.IgnoreQueryFilters().AsNoTracking().Where(block => block.OwnerId == user).OrderBy(block => block.Id).Select(block => block.Id).ToList());

    private List<SessionCompleted> EventsOf(Guid sessionId) =>
        Query(db => db.Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.Subject == $"{InfrastructureExtensions.ServiceKey}.sessioncompleted")
            .ToList())
            .Select(message => JsonSerializer.Deserialize<SessionCompleted>(message.Payload, JsonSerializerOptions.Web)!)
            .Where(sent => sent.SessionId == sessionId)
            .ToList();

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

    private Task<(HttpStatusCode Status, Session? Session)> StartBonusAsync(string token) => PostAsync(token, "/sessions/bonus");

    private async Task<(HttpStatusCode Status, string? Title)> ProblemAsync(string token)
    {
        using var client = Client(token);
        using var response = await client.PostAsync("/sessions/bonus", null, TestContext.Current.CancellationToken);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (response.StatusCode, problem.GetProperty("title").GetString());
    }

    private async Task<(HttpStatusCode Status, Session? Session)> PostAsync(string token, string path, object? body = null)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync(path, body ?? new { }, TestContext.Current.CancellationToken);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Session>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private async Task<(HttpStatusCode Status, Session? Session)> GetAsync(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Session>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private sealed record Plan(DateOnly StudyDay, List<PlannedBlock> Blocks, Guid? NextBlockId, string DailyGoal);

    private sealed record PlannedBlock(Guid Id, Guid TopicId, string Kind, int Minutes, bool Done);

    private sealed record Session(
        Guid Id, Guid BlockId, Guid TopicId, string Kind, int Minutes, DateOnly StudyDay, DateTime StartedAt, bool Existing, string Outcome, bool Bonus, List<Step> Steps);

    private sealed record Step(string Kind, bool Empty, int? Questions);

    private sealed record Result(int Answered, int Correct, int Wrong, bool Bonus);
}