using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging.Abstractions;

namespace Edvaniq.Services.Planning.UnitTests;

// The handler keeps the topic ids of the newest state of a learning goal and nothing else.
public class TopicsChangedHandlerTests
{
    private static readonly Guid Goal = Guid.NewGuid();
    private static readonly DateTime First = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FirstState_AddsItsTopics_AndRemembersIt()
    {
        var store = new FakeKnownTopics();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        await HandleAsync(store, Changed(First, [a, b]));

        Assert.Equal([(a, false), (b, false)], store.Topics.Select(topic => (topic.TopicId, topic.Hidden)));
        Assert.All(store.Topics, topic => Assert.Equal(("alice", Goal), (topic.OwnerId, topic.LearningGoalId)));
        Assert.Equal(First, Assert.Single(store.Snapshots).ChangedAt);
    }

    [Fact]
    public async Task NewerState_AddsNewTopics_DropsGoneOnes_AndMarksHiddenOnes()
    {
        var store = new FakeKnownTopics();
        var (kept, gone, hidden, added) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await HandleAsync(store, Changed(First, [kept, gone, hidden]));

        await HandleAsync(store, Changed(First.AddSeconds(1), [kept, added], [hidden]));

        Assert.Equal(
            [(kept, false), (hidden, true), (added, false)],
            store.Topics.Select(topic => (topic.TopicId, topic.Hidden)));
        Assert.Equal(First.AddSeconds(1), Assert.Single(store.Snapshots).ChangedAt);
    }

    // The same event under a new message id, and an older state that arrives late.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task StateNotNewer_ChangesNothing(int seconds)
    {
        var store = new FakeKnownTopics();
        var known = Guid.NewGuid();
        await HandleAsync(store, Changed(First, [known]));

        await HandleAsync(store, Changed(First.AddSeconds(seconds), [Guid.NewGuid()]));

        Assert.Equal([known], store.Topics.Select(topic => topic.TopicId));
        Assert.Equal(First, Assert.Single(store.Snapshots).ChangedAt);
    }

    // The column keeps microseconds, so the redelivered event must not count as newer than what was stored.
    [Fact]
    public async Task StateWithinTheSameMicrosecond_IsNoNewerState()
    {
        var store = new FakeKnownTopics();
        var known = Guid.NewGuid();
        await HandleAsync(store, Changed(First.AddTicks(5), [known]));

        await HandleAsync(store, Changed(First.AddTicks(9), [Guid.NewGuid()]));

        Assert.Equal([known], store.Topics.Select(topic => topic.TopicId));
    }

    [Fact]
    public async Task TopicOfAnotherGoal_MovesWithTheEvent_AndStaysWhenTheOldGoalForgetsIt()
    {
        var store = new FakeKnownTopics();
        var other = Guid.NewGuid();
        var topic = Guid.NewGuid();
        await HandleAsync(store, Changed(First, [topic], goal: other));

        await HandleAsync(store, Changed(First.AddSeconds(1), [topic]));
        await HandleAsync(store, Changed(First.AddSeconds(2), [], goal: other));

        Assert.Equal(Goal, Assert.Single(store.Topics).LearningGoalId);
    }

    [Fact]
    public async Task StateOfAnotherUser_LeavesTheTopicsOfTheFirstAlone()
    {
        var store = new FakeKnownTopics();
        var ofAlice = Guid.NewGuid();
        await HandleAsync(store, Changed(First, [ofAlice]));

        await HandleAsync(store, Changed(First, [Guid.NewGuid()]) with { UserId = "bob" });

        Assert.Equal(2, store.Topics.Count);
        Assert.Equal(2, store.Snapshots.Count);
        Assert.Contains(store.Topics, topic => (topic.OwnerId, topic.TopicId) == ("alice", ofAlice));
    }

    // #909: an active topic with an active subtopic is a foundation, the subtopic itself and a topic without one aren't.
    [Fact]
    public async Task TopicWithAnActiveSubtopic_IsAFoundation()
    {
        var store = new FakeKnownTopics();
        var (parent, child, flat) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await HandleAsync(store, Changed(First, [parent, child, flat], parents: [new(child, parent)]));

        Assert.Equal([parent], store.Topics.Where(topic => topic.IsFoundation).Select(topic => topic.TopicId));
    }

    // A message from before #909 has no parents, so there are no foundations.
    [Fact]
    public async Task StateWithoutParents_HasNoFoundations()
    {
        var store = new FakeKnownTopics();
        var (parent, child) = (Guid.NewGuid(), Guid.NewGuid());

        await HandleAsync(store, Changed(First, [parent, child]));

        Assert.DoesNotContain(store.Topics, topic => topic.IsFoundation);
    }

    // The foundation stays one while one active subtopic is left, and stops being one when the last is hidden or gone.
    [Fact]
    public async Task Foundation_LosesIt_WhenItsLastActiveSubtopicIsHiddenOrGone()
    {
        var store = new FakeKnownTopics();
        var (parent, first, second) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await HandleAsync(store, Changed(First, [parent, first, second], parents: [new(first, parent), new(second, parent)]));

        await HandleAsync(store, Changed(First.AddSeconds(1), [parent, first], [second], [new(first, parent), new(second, parent)]));
        var withOne = IsFoundation(store, parent);
        await HandleAsync(store, Changed(First.AddSeconds(2), [parent], [second], [new(second, parent)]));
        var hiddenOnly = IsFoundation(store, parent);
        await HandleAsync(store, Changed(First.AddSeconds(3), [parent, first], parents: [new(first, parent)]));
        var again = IsFoundation(store, parent);
        await HandleAsync(store, Changed(First.AddSeconds(4), [parent]));

        Assert.Equal((true, false, true, false), (withOne, hiddenOnly, again, IsFoundation(store, parent)));
    }

    // A hidden topic is no foundation, even with active subtopics: it isn't planned.
    [Fact]
    public async Task HiddenTopic_IsNoFoundation()
    {
        var store = new FakeKnownTopics();
        var (parent, child) = (Guid.NewGuid(), Guid.NewGuid());

        await HandleAsync(store, Changed(First, [child], [parent], [new(child, parent)]));

        Assert.False(IsFoundation(store, parent));
    }

    // #380: rank and size come with every state, hidden topics too, and the next state replaces them.
    [Fact]
    public async Task RankAndSize_AreTakenOver_AndFollowTheNextState()
    {
        var store = new FakeKnownTopics();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await HandleAsync(store, Changed(First, [a, b], [c]) with { Topics = [new(b, 0, 4), new(a, 1, 2), new(c, 2, 3)] });

        Assert.Equal([(a, 1, 2), (b, 0, 4), (c, 2, 3)], store.Topics.Select(topic => (topic.TopicId, topic.Rank, topic.Size)).OrderBy(topic => topic.TopicId == a ? 0 : topic.TopicId == b ? 1 : 2));
        Assert.Equal([b, a], await store.ActiveAsync("alice", Goal, TestContext.Current.CancellationToken));

        await HandleAsync(store, Changed(First.AddSeconds(1), [a, b, c]) with { Topics = [new(a, 0, 0), new(c, 1, 5), new(b, 2, 4)] });

        Assert.Equal([a, c, b], await store.ActiveAsync("alice", Goal, TestContext.Current.CancellationToken));
        Assert.Equal(
            new Dictionary<Guid, int> { [a] = 1, [b] = 4, [c] = 5 },
            await store.SizesAsync("alice", Goal, TestContext.Current.CancellationToken));
    }

    // A message from before #380 has no facts: no rank, size 1.
    [Fact]
    public async Task StateWithoutFacts_GivesNoRank_AndSizeOne()
    {
        var store = new FakeKnownTopics();
        var topic = Guid.NewGuid();
        await HandleAsync(store, Changed(First, [topic]) with { Topics = [new(topic, 3, 7)] });

        await HandleAsync(store, Changed(First.AddSeconds(1), [topic]));

        Assert.Equal((null, 1), (Assert.Single(store.Topics).Rank, store.Topics[0].Size));
    }

    // #391: one topic hidden, shown again, then gone. Only an active one is for the plan.
    [Fact]
    public async Task Topic_HiddenThenShownThenGone_IsPlannedOnlyWhileActive()
    {
        var store = new FakeKnownTopics();
        var (topic, other) = (Guid.NewGuid(), Guid.NewGuid());
        var cancel = TestContext.Current.CancellationToken;
        await HandleAsync(store, Changed(First, [topic, other]));

        await HandleAsync(store, Changed(First.AddSeconds(1), [other], [topic]));
        Assert.Equal([other], await store.ActiveAsync("alice", Goal, cancel));
        Assert.True(store.Topics.Single(known => known.TopicId == topic).Hidden);

        await HandleAsync(store, Changed(First.AddSeconds(2), [topic, other]));
        Assert.Equal(new[] { topic, other }.Order(), (await store.ActiveAsync("alice", Goal, cancel)).Order());

        await HandleAsync(store, Changed(First.AddSeconds(3), [other]));
        Assert.Equal([other], await store.ActiveAsync("alice", Goal, cancel));
        Assert.DoesNotContain(store.Topics, known => known.TopicId == topic);
    }

    // #412: a topic gone from the goal takes its done blocks along, on every day, and of no other topic, goal or user.
    [Fact]
    public async Task RemovedTopic_TakesItsDoneBlocksAlong()
    {
        var store = new FakeKnownTopics();
        var cleanup = new FakeCleanup();
        var (gone, kept) = (Guid.NewGuid(), Guid.NewGuid());
        await HandleAsync(store, Changed(First, [gone, kept]), cleanup);
        var ofKept = Done(kept);
        var ofOtherGoal = Done(gone, goal: Guid.NewGuid());
        var ofBob = Done(gone, owner: "bob");
        cleanup.DoneBlocks.AddRange([Done(gone), ofKept, ofOtherGoal, ofBob]);

        await HandleAsync(store, Changed(First.AddSeconds(1), [kept]), cleanup);

        Assert.Equal([ofKept, ofOtherGoal, ofBob], cleanup.DoneBlocks);
        Assert.Empty(cleanup.Outbox.Events);
    }

    // Hidden is no removal, a topic that moved to another goal neither, and an older state changes nothing.
    [Fact]
    public async Task HiddenMovedOrOlder_ClearsNothingUp()
    {
        var store = new FakeKnownTopics();
        var cleanup = new FakeCleanup();
        var (hidden, moved, late) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await HandleAsync(store, Changed(First, [hidden, moved, late]), cleanup);
        cleanup.DoneBlocks.AddRange([Done(hidden), Done(moved), Done(late)]);
        cleanup.Sessions.Add(Running(hidden));

        await HandleAsync(store, Changed(First.AddSeconds(1), [moved], goal: Guid.NewGuid()), cleanup);
        await HandleAsync(store, Changed(First.AddSeconds(2), [late], [hidden]), cleanup);
        await HandleAsync(store, Changed(First.AddSeconds(1), []), cleanup);

        Assert.Equal(0, cleanup.Calls);
        Assert.Equal(3, cleanup.DoneBlocks.Count);
        Assert.True(cleanup.Sessions[0].IsActive);
    }

    // The session running on the topic is broken off and says so, a bonus review too. Ended sessions stay as they are.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovedTopic_BreaksOffTheSessionRunningOnIt(bool bonus)
    {
        var store = new FakeKnownTopics();
        var cleanup = new FakeCleanup();
        var gone = Guid.NewGuid();
        await HandleAsync(store, Changed(First, [gone]), cleanup);
        var ended = Running(gone);
        ended.Complete(First.AddMinutes(10));
        var running = bonus ? Session.StartBonus(Guid.NewGuid(), "alice", Goal, Guid.NewGuid(), Today, gone, First.AddMinutes(20)) : Running(gone);
        cleanup.Sessions.AddRange([ended, running]);

        await HandleAsync(store, Changed(First.AddSeconds(1), []), cleanup);

        Assert.Equal((SessionOutcome.Aborted, Now), (running.Outcome, running.EndedAt));
        Assert.Equal(SessionOutcome.Completed, ended.Outcome);
        var sent = Assert.IsType<SessionCompleted>(Assert.Single(cleanup.Outbox.Events));
        Assert.Equal(Fields(SessionEnds.EventOf(running, running.UnitStartedAt(cleanup.Sessions))), Fields(sent));
        Assert.Equal((running.Id, false, Now), (sent.SessionId, sent.Completed, sent.EndedAt));
    }

    // A session on a topic that stays, in another goal or of another user runs on.
    [Fact]
    public async Task SessionOnAnotherTopicGoalOrUser_RunsOn()
    {
        var gone = Guid.NewGuid();
        foreach (var session in new[] { Running(Guid.NewGuid()), Running(gone, goal: Guid.NewGuid()), Running(gone, owner: "bob") })
        {
            var store = new FakeKnownTopics();
            var cleanup = new FakeCleanup();
            cleanup.Sessions.Add(session);
            await HandleAsync(store, Changed(First, [gone]), cleanup);

            await HandleAsync(store, Changed(First.AddSeconds(1), []), cleanup);

            Assert.True(session.IsActive);
            Assert.Empty(cleanup.Outbox.Events);
        }
    }

    private static TopicsChanged Changed(
        DateTime changedAt, Guid[] topicIds, Guid[]? hidden = null, TopicParent[]? parents = null, Guid? goal = null) =>
        new("alice", goal ?? Goal, changedAt, topicIds, hidden ?? [], parents);

    private static bool IsFoundation(FakeKnownTopics store, Guid topicId) =>
        store.Topics.Single(topic => topic.TopicId == topicId).IsFoundation;

    private static Task HandleAsync(FakeKnownTopics store, TopicsChanged integrationEvent, FakeCleanup? cleanup = null) =>
        new TopicsChangedHandler(store, cleanup ?? new FakeCleanup(), cleanup?.Outbox ?? new FakeOutbox(), new FixedTime(Now), NullLogger<TopicsChangedHandler>.Instance)
            .HandleAsync(integrationEvent, TestContext.Current.CancellationToken);

    private static readonly DateTime Now = First.AddHours(1);
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static DoneBlock Done(Guid topic, string owner = "alice", Guid? goal = null) =>
        DoneBlock.Create(Guid.NewGuid(), owner, goal ?? Goal, Today, new Block(topic, 15), First);

    private static Session Running(Guid topic, string owner = "alice", Guid? goal = null) =>
        Session.Start(Guid.NewGuid(), owner, goal ?? Goal, Today, DayBlocks.WithIds(owner, goal ?? Goal, Today, [new Block(topic, 15)])[0], First);

    private sealed class FakeCleanup : ITopicCleanup
    {
        public List<DoneBlock> DoneBlocks { get; } = [];

        public List<Session> Sessions { get; } = [];

        public FakeOutbox Outbox { get; } = new();

        public int Calls { get; private set; }

        public Task<int> RemoveDoneBlocksAsync(string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken)
        {
            Calls++;
            var count = DoneBlocks.RemoveAll(block => block.OwnerId == ownerId && block.LearningGoalId == learningGoalId && topicIds.Contains(block.TopicId));
            return Task.FromResult(count);
        }

        public Task<Session?> ActiveSessionAsync(string ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.FirstOrDefault(session => session.OwnerId == ownerId && session.IsActive));

        public Task<IReadOnlyList<Session>> OnBlockAsync(string ownerId, Guid blockId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Session>>([.. Sessions.Where(session => session.OwnerId == ownerId && session.BlockId == blockId)]);
    }

    private static (string, Guid, DateTime, bool, string, DateTime?) Fields(SessionCompleted sent) =>
        (sent.UserId, sent.SessionId, sent.EndedAt, sent.Completed, string.Join(',', sent.IntroducedTopicIds), sent.UnitStartedAt);

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeOutbox : IEventOutbox
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public void Add(IIntegrationEvent integrationEvent) => Events.Add(integrationEvent);
    }

    private sealed class FakeKnownTopics : IKnownTopics
    {
        public List<KnownTopic> Topics { get; } = [];

        public List<TopicsSnapshot> Snapshots { get; } = [];

        public Task<TopicsSnapshot?> SnapshotAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshots.FirstOrDefault(snapshot => snapshot.OwnerId == ownerId && snapshot.LearningGoalId == learningGoalId));

        public Task<IReadOnlyList<KnownTopic>> ListAsync(
            string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnownTopic>>(
                [.. Topics.Where(topic => topic.OwnerId == ownerId && (topic.LearningGoalId == learningGoalId || topicIds.Contains(topic.TopicId)))]);

        public Task<IReadOnlyList<Guid>> ActiveAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                [.. Active(ownerId, learningGoalId).OrderBy(topic => topic.Rank is null).ThenBy(topic => topic.Rank).ThenBy(topic => topic.TopicId).Select(topic => topic.TopicId)]);

        public Task<IReadOnlyDictionary<Guid, int>> SizesAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(Active(ownerId, learningGoalId).ToDictionary(topic => topic.TopicId, topic => topic.Size));

        private IEnumerable<KnownTopic> Active(string ownerId, Guid learningGoalId) =>
            Topics.Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId && !topic.Hidden);

        public Task<IReadOnlySet<Guid>> FoundationsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(
                Topics.Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId && !topic.Hidden && topic.IsFoundation).Select(topic => topic.TopicId).ToHashSet());

        public void Add(TopicsSnapshot snapshot) => Snapshots.Add(snapshot);

        public void Add(KnownTopic topic) => Topics.Add(topic);

        public void Remove(KnownTopic topic) => Topics.Remove(topic);
    }
}