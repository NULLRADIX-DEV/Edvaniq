using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #374: a plan asks LearningEngine and Assessment side by side, at most once each, and one of them failing never
// stops the other or the plan.
public class TodayPlansTests
{
    private const string Alice = "alice";

    private static readonly Guid Ready = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Pending = new("00000000-0000-0000-0000-00000000000b");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // AC1: each source only answers once the other has been asked too. Asked one after the other, the first would
    // wait in vain, so a plan here proves both calls are open at the same time, and two calls is all there are.
    [Fact]
    public async Task BothSources_AreAskedOnce_AtTheSameTime()
    {
        var bothAsked = new Barrier(2);
        var mastery = new FakeMastery(() => bothAsked.SignalAndWait(Patience) ? Standings() : throw new TimeoutException());
        var pools = new FakePools(() => bothAsked.SignalAndWait(Patience) ? new HashSet<Guid> { Ready } : throw new TimeoutException());

        var today = await Plans(mastery, pools).TodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, mastery.Calls);
        Assert.Equal(1, pools.Calls);
        Assert.True(today.MasteryAvailable);
        Assert.True(today.QuestionPoolsAvailable);
        Assert.Equal([Ready], today.Plan.Blocks.Select(block => block.TopicId).Distinct());
        Assert.Equal([Pending], today.Plan.NotReady);
    }

    // AC2: Assessment throws, LearningEngine's answer still counts, the plan comes with every topic and says the pools
    // are missing.
    [Fact]
    public async Task AssessmentThrows_LearningEngineStillAnswers()
    {
        var mastery = new FakeMastery(Standings);
        var pools = new FakePools(() => throw new HttpRequestException("down"));

        var today = await Plans(mastery, pools).TodayAsync(TestContext.Current.CancellationToken);

        Assert.True(today.MasteryAvailable);
        Assert.False(today.QuestionPoolsAvailable);
        Assert.Equal(1, mastery.Calls);
        Assert.Equal(new[] { Ready, Pending }.Order(), today.Plan.Blocks.Select(block => block.TopicId).Distinct().Order());
        Assert.Equal(Pending, today.Plan.Blocks.First(block => block.Kind == BlockKind.Learn).TopicId);
    }

    [Fact]
    public async Task LearningEngineThrows_AssessmentStillAnswers()
    {
        var pools = new FakePools(() => new HashSet<Guid> { Ready });

        var today = await Plans(new FakeMastery(() => throw new InvalidOperationException()), pools).TodayAsync(TestContext.Current.CancellationToken);

        Assert.False(today.MasteryAvailable);
        Assert.True(today.QuestionPoolsAvailable);
        Assert.Equal([Ready], today.Plan.Blocks.Select(block => block.TopicId).Distinct());
    }

    [Fact]
    public async Task BothDown_StillGiveAPlan()
    {
        var today = await Plans(new FakeMastery(() => null), new FakePools(() => null)).TodayAsync(TestContext.Current.CancellationToken);

        Assert.False(today.MasteryAvailable);
        Assert.False(today.QuestionPoolsAvailable);
        Assert.NotEmpty(today.Plan.Blocks);
    }

    [Fact]
    public async Task WithoutTopics_NobodyIsAsked()
    {
        var mastery = new FakeMastery(Standings);
        var pools = new FakePools(() => new HashSet<Guid>());

        var today = await Plans(mastery, pools, topics: []).TodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, mastery.Calls + pools.Calls);
        Assert.Equal(TodayPlanEmpty.NoTopics, today.Plan.Empty);
        Assert.True(today.MasteryAvailable);
        Assert.True(today.QuestionPoolsAvailable);
    }

    private static Dictionary<Guid, TopicStanding> Standings() => new()
    {
        [Ready] = new(70, TopicState.Building),
        [Pending] = new(10, TopicState.Weakness),
    };

    private static TodayPlans Plans(FakeMastery mastery, FakePools pools, IReadOnlyList<Guid>? topics = null) =>
        new(new FakeTopics(topics ?? [Ready, Pending]), mastery, pools, new FakeBudgets(), new NoDoneBlocks(), new FakeUser(), TimeProvider.System);

    // Runs the answer on the thread pool, the way an HTTP call returns, so a waiting answer doesn't hold up the other.
    private sealed class FakeMastery(Func<IReadOnlyDictionary<Guid, TopicStanding>?> answer) : IMasterySource
    {
        private int calls;

        public int Calls => calls;

        public Task<IReadOnlyDictionary<Guid, TopicStanding>?> OfLearningGoalAsync(Guid learningGoalId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.Run(answer, cancellationToken);
        }
    }

    private sealed class FakePools(Func<IReadOnlySet<Guid>?> answer) : IQuestionPoolSource
    {
        private int calls;

        public int Calls => calls;

        public Task<IReadOnlySet<Guid>?> ReadyTopicsAsync(Guid learningGoalId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.Run(answer, cancellationToken);
        }
    }

    private sealed class FakeTopics(IReadOnlyList<Guid> active) : IKnownTopics
    {
        public Task<IReadOnlyList<Guid>> ActiveAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult(active);

        public Task<IReadOnlySet<Guid>> FoundationsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<TopicsSnapshot?> SnapshotAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KnownTopic>> ListAsync(
            string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(TopicsSnapshot snapshot) => throw new NotSupportedException();

        public void Add(KnownTopic topic) => throw new NotSupportedException();

        public void Remove(KnownTopic topic) => throw new NotSupportedException();
    }

    private sealed class NoDoneBlocks : IDoneBlocks
    {
        public Task<IReadOnlyList<DoneBlock>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DoneBlock>>([]);

        public Task AddAsync(DoneBlock block, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeBudgets : IDailyBudgets
    {
        public DailyBudget Default { get; } = DailyBudget.Uniform(30);

        public Task<DailyBudget> ForUserAsync(string ownerId, CancellationToken cancellationToken) => Task.FromResult(Default);

        public Task<DailyBudget?> StoredAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetAsync(DailyBudget budget, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeUser : ICurrentUser
    {
        public string Id => Alice;
    }
}