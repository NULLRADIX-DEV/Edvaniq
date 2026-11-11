using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Assessment.UnitTests;

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

    private static TopicsChanged Changed(DateTime changedAt, Guid[] topicIds, Guid[]? hidden = null, Guid? goal = null) =>
        new("alice", goal ?? Goal, changedAt, topicIds, hidden ?? []);

    private static Task HandleAsync(FakeKnownTopics store, TopicsChanged integrationEvent) =>
        new TopicsChangedHandler(store).HandleAsync(integrationEvent, TestContext.Current.CancellationToken);

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

        public void Add(TopicsSnapshot snapshot) => Snapshots.Add(snapshot);

        public void Add(KnownTopic topic) => Topics.Add(topic);

        public void Remove(KnownTopic topic) => Topics.Remove(topic);
    }
}