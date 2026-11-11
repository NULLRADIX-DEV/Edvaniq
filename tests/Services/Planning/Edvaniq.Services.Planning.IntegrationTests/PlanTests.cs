using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// GET /plan/today: the blocks of today for the topics of the user's default learning goal, weaker topics first, with
// the mastery asked from LearningEngine in exactly one call with the caller's own token.
public sealed class PlanTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task Plan_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.GetAsync("/plan/today", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // AC1 and AC3, two users side by side: each gets blocks for their own topics only, weakest first, and LearningEngine
    // is asked once per plan, with the token of the user whose plan it is.
    [Fact]
    public async Task Plan_HasBlocksOfTheOwnTopics_WeakestFirst_AskingLearningEngineOnce()
    {
        var (alice, aliceToken) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        var (strong, weak) = (Guid.NewGuid(), Guid.NewGuid());
        var bobsTopic = Guid.NewGuid();
        Seed(KnownTopic.Create(alice, strong, LearningGoal.DefaultIdFor(alice), hidden: false));
        Seed(KnownTopic.Create(alice, weak, LearningGoal.DefaultIdFor(alice), hidden: false));
        Seed(KnownTopic.Create(bob, bobsTopic, LearningGoal.DefaultIdFor(bob), hidden: false));
        service.LearningEngine.Set(aliceToken, strong, 70);
        service.LearningEngine.Set(aliceToken, weak, 10);
        service.LearningEngine.Set(bobToken, bobsTopic, 40);

        var alicePlan = await PlanAsync(aliceToken);
        var bobPlan = await PlanAsync(bobToken);

        Assert.Equal(weak, alicePlan.Blocks[0].TopicId);
        Assert.Equal(new[] { strong, weak }.Order(), alicePlan.Blocks.Select(block => block.TopicId).Distinct().Order());
        Assert.True(MinutesOf(alicePlan, weak) > MinutesOf(alicePlan, strong));
        Assert.Null(alicePlan.EmptyReason);
        Assert.True(alicePlan.MasteryAvailable);
        Assert.Empty(alicePlan.ShortenedTopicIds);
        Assert.Equal(LearningGoal.DefaultIdFor(alice), alicePlan.LearningGoalId);
        Assert.Equal([bobsTopic], bobPlan.Blocks.Select(block => block.TopicId).Distinct());

        var aliceCalls = service.LearningEngine.RequestsWith(aliceToken);
        Assert.Equal($"/mastery?learningGoalId={LearningGoal.DefaultIdFor(alice)}", Assert.Single(aliceCalls).PathAndQuery);
        Assert.Equal($"/mastery?learningGoalId={LearningGoal.DefaultIdFor(bob)}", Assert.Single(service.LearningEngine.RequestsWith(bobToken)).PathAndQuery);
    }

    // #369: the day starts with the review block, the rest are learn blocks, the kind in lower case.
    [Fact]
    public async Task Plan_StartsWithAReviewBlock_TheOthersAreLearnBlocks()
    {
        var (alice, token) = NewUser("alice");
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        Seed(KnownTopic.Create(alice, first, LearningGoal.DefaultIdFor(alice), hidden: false));
        Seed(KnownTopic.Create(alice, second, LearningGoal.DefaultIdFor(alice), hidden: false));
        service.LearningEngine.Set(token, first, 20);
        service.LearningEngine.Set(token, second, 40);

        var plan = await PlanAsync(token);

        Assert.Equal(new PlannedBlock(first, 5, "review"), plan.Blocks[0]);
        Assert.All(plan.Blocks.Skip(1), block => Assert.Equal("learn", block.Kind));
        Assert.Equal(30, plan.Blocks.Sum(block => block.Minutes));
    }

    // #367: the state comes from LearningEngine over HTTP. The same mastery as new gives no review, as weakness one, so
    // a changed threshold in LearningEngine reaches the plan without a change here.
    [Fact]
    public async Task Plan_TakesTheStateFromLearningEngine()
    {
        var (alice, aliceToken) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        var (alicesTopic, bobsTopic) = (Guid.NewGuid(), Guid.NewGuid());
        Seed(KnownTopic.Create(alice, alicesTopic, LearningGoal.DefaultIdFor(alice), hidden: false));
        Seed(KnownTopic.Create(bob, bobsTopic, LearningGoal.DefaultIdFor(bob), hidden: false));
        service.LearningEngine.Set(aliceToken, alicesTopic, 50, "new");
        service.LearningEngine.Set(bobToken, bobsTopic, 50, "weakness");

        var alicePlan = await PlanAsync(aliceToken);
        var bobPlan = await PlanAsync(bobToken);

        Assert.All(alicePlan.Blocks, block => Assert.Equal("learn", block.Kind));
        Assert.Equal(new PlannedBlock(bobsTopic, 5, "review"), bobPlan.Blocks[0]);
    }

    // A state Planning doesn't know yet still gives a plan, with the mastery.
    [Fact]
    public async Task Plan_WithAStatePlanningDoesNotKnow_StillComes()
    {
        var (alice, token) = NewUser("alice");
        var topic = Guid.NewGuid();
        Seed(KnownTopic.Create(alice, topic, LearningGoal.DefaultIdFor(alice), hidden: false));
        service.LearningEngine.Set(token, topic, 40, "forgotten");

        var plan = await PlanAsync(token);

        Assert.True(plan.MasteryAvailable);
        Assert.Equal([topic], plan.Blocks.Select(block => block.TopicId).Distinct());
    }

    // #370: 13 topics don't get a block each in a week of 30 minutes a day. The weakness from LearningEngine stays,
    // two topics in progress are shortened, and the answer names them.
    [Fact]
    public async Task Plan_ShortOfTime_NamesTheShortenedTopics_ButNeverTheWeakness()
    {
        var (alice, token) = NewUser("alice");
        var weakness = Guid.NewGuid();
        var inProgress = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToList();
        foreach (var topic in inProgress.Append(weakness))
        {
            Seed(KnownTopic.Create(alice, topic, LearningGoal.DefaultIdFor(alice), hidden: false));
            service.LearningEngine.Set(token, topic, 50);
        }

        service.LearningEngine.Set(token, weakness, 70, "weakness");

        var plan = await PlanAsync(token);

        Assert.Equal(2, plan.ShortenedTopicIds.Count);
        Assert.All(plan.ShortenedTopicIds, id => Assert.Contains(id, inProgress));
        Assert.Equal(plan.ShortenedTopicIds.Order(), plan.ShortenedTopicIds);
    }

    [Fact]
    public async Task Plan_TakesTheStudyDayOfNow()
    {
        var (alice, token) = NewUser("alice");
        Seed(KnownTopic.Create(alice, Guid.NewGuid(), LearningGoal.DefaultIdFor(alice), hidden: false));

        var before = StudyDayNow();
        var plan = await PlanAsync(token);
        var after = StudyDayNow();

        Assert.InRange(plan.StudyDay, before, after);
    }

    // AC2: no topics, no blocks, and a reason the client can show as a hint. Nothing to ask LearningEngine about.
    [Fact]
    public async Task Plan_WithoutTopics_IsEmpty_WithTheNoTopicsHint()
    {
        var (_, token) = NewUser("alice");

        var plan = await PlanAsync(token);

        Assert.Empty(plan.Blocks);
        Assert.Equal("noTopics", plan.EmptyReason);
        Assert.True(plan.MasteryAvailable);
        Assert.Empty(service.LearningEngine.RequestsWith(token));
    }

    [Fact]
    public async Task Plan_LeavesOutHiddenTopics_AndThoseOfAnotherGoal()
    {
        var (alice, token) = NewUser("alice");
        var (shown, hidden, elsewhere) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Seed(KnownTopic.Create(alice, shown, LearningGoal.DefaultIdFor(alice), hidden: false));
        Seed(KnownTopic.Create(alice, hidden, LearningGoal.DefaultIdFor(alice), hidden: true));
        Seed(KnownTopic.Create(alice, elsewhere, Guid.NewGuid(), hidden: false));

        var plan = await PlanAsync(token);

        Assert.Equal([shown], plan.Blocks.Select(block => block.TopicId).Distinct());
    }

    [Fact]
    public async Task Plan_WithEveryTopicMastered_IsEmpty_WithTheAllMasteredHint()
    {
        var (alice, token) = NewUser("alice");
        var topic = Guid.NewGuid();
        Seed(KnownTopic.Create(alice, topic, LearningGoal.DefaultIdFor(alice), hidden: false));
        service.LearningEngine.Set(token, topic, 100);

        var plan = await PlanAsync(token);

        Assert.Empty(plan.Blocks);
        Assert.Equal("allMastered", plan.EmptyReason);
    }

    // A92: without LearningEngine the plan still comes, only without weaker topics first, and says so.
    [Fact]
    public async Task Plan_WhileLearningEngineIsUnreachable_ComesWithoutMastery()
    {
        var (alice, token) = NewUser("alice");
        var topic = Guid.NewGuid();
        Seed(KnownTopic.Create(alice, topic, LearningGoal.DefaultIdFor(alice), hidden: false));
        service.LearningEngine.Unreachable(token);

        var plan = await PlanAsync(token);

        Assert.Equal([topic], plan.Blocks.Select(block => block.TopicId).Distinct());
        Assert.False(plan.MasteryAvailable);
    }

    // #374 AC1: one plan asks LearningEngine and Assessment once each, both open at the same time, never more than 3.
    [Fact]
    public async Task Plan_AsksLearningEngineAndAssessment_SideBySide_OnceEach()
    {
        var (alice, token) = NewUser("alice");
        var goal = LearningGoal.DefaultIdFor(alice);
        Seed(KnownTopic.Create(alice, Guid.NewGuid(), goal, hidden: false));
        service.Calls.WaitForEachOther(token);

        var plan = await PlanAsync(token);

        Assert.True(plan.MasteryAvailable);
        Assert.True(plan.QuestionPoolsAvailable);
        Assert.Equal(2, service.Calls.Total(token));
        Assert.Equal(2, service.Calls.MostAtOnce(token));
        Assert.Equal($"/question-pools?learningGoalId={goal}", Assert.Single(service.Assessment.RequestsWith(token)).PathAndQuery);
        Assert.Single(service.LearningEngine.RequestsWith(token));
    }

    // #374: a topic whose question pool isn't ready gets no block, even as the weakest, and the answer names it. One
    // Assessment doesn't name counts the same.
    [Fact]
    public async Task Plan_LeavesOutTopicsWithoutReadyQuestionPool_AndNamesThem()
    {
        var (alice, token) = NewUser("alice");
        var goal = LearningGoal.DefaultIdFor(alice);
        var (ready, pending, unknown) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Seed(KnownTopic.Create(alice, ready, goal, hidden: false));
        Seed(KnownTopic.Create(alice, pending, goal, hidden: false));
        Seed(KnownTopic.Create(alice, unknown, goal, hidden: false), withQuestionPool: false);
        service.Assessment.Set(token, pending, "notReady");
        service.LearningEngine.Set(token, ready, 70);
        service.LearningEngine.Set(token, pending, 5, "weakness");

        var plan = await PlanAsync(token);

        Assert.Equal([ready], plan.Blocks.Select(block => block.TopicId).Distinct());
        Assert.Equal(new[] { pending, unknown }.Order(), plan.NotReadyTopicIds);
        Assert.True(plan.QuestionPoolsAvailable);
        Assert.Null(plan.EmptyReason);
    }

    [Fact]
    public async Task Plan_WithNoReadyQuestionPool_IsEmpty_WithTheQuestionsPendingHint()
    {
        var (alice, token) = NewUser("alice");
        var topic = Guid.NewGuid();
        Seed(KnownTopic.Create(alice, topic, LearningGoal.DefaultIdFor(alice), hidden: false));
        service.Assessment.Set(token, topic, "notReady");

        var plan = await PlanAsync(token);

        Assert.Empty(plan.Blocks);
        Assert.Equal("questionsPending", plan.EmptyReason);
        Assert.Equal([topic], plan.NotReadyTopicIds);
    }

    // #374 AC2: without Assessment the plan still comes, never a 500, with every topic and the mastery, and says so.
    // LearningEngine is asked all the same.
    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Plan_WhileAssessmentIsDown_ComesWithEveryTopic(HttpStatusCode? status)
    {
        var (alice, token) = NewUser("alice");
        var (strong, weak) = (Guid.NewGuid(), Guid.NewGuid());
        Seed(KnownTopic.Create(alice, strong, LearningGoal.DefaultIdFor(alice), hidden: false));
        Seed(KnownTopic.Create(alice, weak, LearningGoal.DefaultIdFor(alice), hidden: false));
        service.Assessment.Set(token, weak, "notReady");
        service.LearningEngine.Set(token, strong, 70);
        service.LearningEngine.Set(token, weak, 10);
        if (status is { } failing)
        {
            service.Assessment.Fail(token, failing);
        }
        else
        {
            service.Assessment.Unreachable(token);
        }

        var plan = await PlanAsync(token);

        Assert.False(plan.QuestionPoolsAvailable);
        Assert.True(plan.MasteryAvailable);
        Assert.Equal(weak, plan.Blocks[0].TopicId);
        Assert.Equal(new[] { strong, weak }.Order(), plan.Blocks.Select(block => block.TopicId).Distinct().Order());
        Assert.Empty(plan.NotReadyTopicIds);
        Assert.Single(service.LearningEngine.RequestsWith(token));
    }

    [Fact]
    public async Task Plan_WhileBothAreDown_StillComes()
    {
        var (alice, token) = NewUser("alice");
        var topic = Guid.NewGuid();
        Seed(KnownTopic.Create(alice, topic, LearningGoal.DefaultIdFor(alice), hidden: false));
        service.Assessment.Unreachable(token);
        service.LearningEngine.Unreachable(token);

        var plan = await PlanAsync(token);

        Assert.False(plan.QuestionPoolsAvailable);
        Assert.False(plan.MasteryAvailable);
        Assert.Equal([topic], plan.Blocks.Select(block => block.TopicId).Distinct());
    }

    // #374 AC3, two users side by side: Assessment is asked with each user's own token, and only their own pools count.
    [Fact]
    public async Task Plan_AsksAssessmentWithTheUsersOwnToken_AndUsesOnlyTheirPools()
    {
        var (alice, aliceToken) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        var shared = Guid.NewGuid();
        Seed(KnownTopic.Create(alice, shared, LearningGoal.DefaultIdFor(alice), hidden: false));
        var bobsTopic = Guid.NewGuid();
        Seed(KnownTopic.Create(bob, bobsTopic, LearningGoal.DefaultIdFor(bob), hidden: false));
        service.Assessment.Set(aliceToken, shared, "notReady");
        service.Assessment.Set(bobToken, shared, "ready");

        var alicePlan = await PlanAsync(aliceToken);
        var bobPlan = await PlanAsync(bobToken);

        Assert.Equal("questionsPending", alicePlan.EmptyReason);
        Assert.Equal([bobsTopic], bobPlan.Blocks.Select(block => block.TopicId).Distinct());
        Assert.Equal(
            $"/question-pools?learningGoalId={LearningGoal.DefaultIdFor(alice)}",
            Assert.Single(service.Assessment.RequestsWith(aliceToken)).PathAndQuery);
        Assert.Equal(
            $"/question-pools?learningGoalId={LearningGoal.DefaultIdFor(bob)}",
            Assert.Single(service.Assessment.RequestsWith(bobToken)).PathAndQuery);
        Assert.DoesNotContain(service.Assessment.Requests, request => request.Token is null);
    }

    // The warning names what went wrong and the status, never the caller's token or what Assessment answered.
    [Fact]
    public async Task FailingAssessment_IsLogged_WithoutToken()
    {
        var (alice, token) = NewUser("alice");
        Seed(KnownTopic.Create(alice, Guid.NewGuid(), LearningGoal.DefaultIdFor(alice), hidden: false));
        service.Assessment.Fail(token, HttpStatusCode.BadGateway);

        var plan = await PlanAsync(token);

        Assert.False(plan.QuestionPoolsAvailable);
        var warning = Assert.Single(service.Logs.Containing("Assessment could not be asked"), entry => entry.Text.Contains("502", StringComparison.Ordinal));
        Assert.Contains(nameof(HttpRequestException), warning.Text, StringComparison.Ordinal);
        Assert.Empty(service.Logs.Containing(token, "boom"));
    }

    // AC4 across calls: the same topics and mastery give the same plan.
    [Fact]
    public async Task Plan_TwiceWithTheSameInput_IsTheSame()
    {
        var (alice, token) = NewUser("alice");
        foreach (var mastery in new[] { 30, 30, 70, 0 })
        {
            var topic = Guid.NewGuid();
            Seed(KnownTopic.Create(alice, topic, LearningGoal.DefaultIdFor(alice), hidden: false));
            service.LearningEngine.Set(token, topic, mastery);
        }

        var first = await PlanAsync(token);
        var second = await PlanAsync(token);

        Assert.Equal(first.Blocks, second.Blocks);
    }

    // The warning names what went wrong and the status, never the caller's token or what LearningEngine answered.
    [Fact]
    public async Task FailingLearningEngine_IsLogged_WithoutTokenOrAnswer()
    {
        var (alice, token) = NewUser("alice");
        Seed(KnownTopic.Create(alice, Guid.NewGuid(), LearningGoal.DefaultIdFor(alice), hidden: false));
        var answer = $"stack trace with secret {Guid.NewGuid():N}";
        service.LearningEngine.Fail(token, HttpStatusCode.InternalServerError, answer);

        var plan = await PlanAsync(token);

        Assert.False(plan.MasteryAvailable);
        var warning = Assert.Single(service.Logs.Containing("LearningEngine could not be asked"), entry => entry.Text.Contains("500", StringComparison.Ordinal));
        Assert.Contains(nameof(HttpRequestException), warning.Text, StringComparison.Ordinal);
        Assert.Empty(service.Logs.Containing(token, answer));
    }

    // The token of each user, so a seeded topic gets a ready question pool in Assessment unless a test says otherwise.
    private readonly Dictionary<string, string> tokens = [];

    private (string User, string Token) NewUser(string name)
    {
        var user = $"{name}-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        tokens[user] = token;
        return (user, token);
    }

    private static int MinutesOf(Plan plan, Guid topic) =>
        plan.Blocks.Where(block => block.TopicId == topic).Sum(block => block.Minutes);

    private static DateOnly StudyDayNow() => SharedKernel.StudyDay.Of(DateTime.UtcNow);

    private async Task<Plan> PlanAsync(string token)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/plan/today", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Plan>(cancellationToken))!;
    }

    private void Seed(KnownTopic topic, bool withQuestionPool = true)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
        db.KnownTopics.Add(topic);
        db.SaveChanges();

        if (withQuestionPool && tokens.TryGetValue(topic.OwnerId, out var token))
        {
            service.Assessment.Set(token, topic.TopicId);
        }
    }

    private sealed record Plan(
        DateOnly StudyDay,
        Guid LearningGoalId,
        List<PlannedBlock> Blocks,
        string? EmptyReason,
        bool MasteryAvailable,
        List<Guid> ShortenedTopicIds,
        bool QuestionPoolsAvailable,
        List<Guid> NotReadyTopicIds);

    private sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind);
}