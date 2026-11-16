using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging.Abstractions;

namespace Edvaniq.Services.Planning.UnitTests;

// #412: a session broken off by the user and one broken off because its topic left the goal send the same event.
public sealed class SessionEndsTests
{
    private const string User = "alice";

    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateTime Started = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Started.AddMinutes(7);
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public async Task AbortByTheUser_AndByARemovedTopic_SendTheSameEvent()
    {
        var cancel = TestContext.Current.CancellationToken;
        var byUser = new Outbox();
        var byTopic = new Outbox();

        var session = Start();
        await new SessionChanges(new OneSession(session), new NoDoneBlocks(), byUser, new FixedTime(Now), NullLogger<SessionChanges>.Instance)
            .AbortAsync(Id, cancel);
        var other = Start();
        var topics = new Topics();
        var handler = new TopicsChangedHandler(topics, new Cleanup(other), byTopic, new FixedTime(Now), NullLogger<TopicsChangedHandler>.Instance);
        await handler.HandleAsync(new TopicsChanged(User, Goal, Started, [Topic], []), cancel);
        await handler.HandleAsync(new TopicsChanged(User, Goal, Started.AddSeconds(1), [], []), cancel);

        var (a, b) = (Assert.IsType<SessionCompleted>(Assert.Single(byUser.Events)), Assert.IsType<SessionCompleted>(Assert.Single(byTopic.Events)));
        Assert.Equal(
            (a.UserId, a.SessionId, a.EndedAt, a.Completed, a.UnitStartedAt),
            (b.UserId, b.SessionId, b.EndedAt, b.Completed, b.UnitStartedAt));
        Assert.Equal(a.IntroducedTopicIds, b.IntroducedTopicIds);
        Assert.Equal((Id, false, Now, Started), (a.SessionId, a.Completed, a.EndedAt, a.UnitStartedAt));
    }

    private static Session Start()
    {
        var block = DayBlocks.WithIds(User, Goal, Today, [new Block(Topic, 15)])[0];
        var session = Session.Start(Id, User, Goal, Today, block, Started, topicIsNew: true);
        session.Advance(1, Started.AddMinutes(3));
        return session;
    }

    private sealed class OneSession(Session session) : ISessions
    {
        public Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Session?>(session);

        public Task<Session?> FindForChangeAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Session?>(session);

        public Task<IReadOnlyList<Session>> OnBlockAsync(Guid blockId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Session>>([session]);

        public Task<bool> TrySaveAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<Session?> ActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Session?> LastAbortedAsync(Guid blockId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Cleanup(Session session) : ITopicCleanup
    {
        public Task<int> RemoveDoneBlocksAsync(string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<Session?> ActiveSessionAsync(string ownerId, CancellationToken cancellationToken) =>
            Task.FromResult<Session?>(session.IsActive ? session : null);

        public Task<IReadOnlyList<Session>> OnBlockAsync(string ownerId, Guid blockId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Session>>([session]);
    }

    private sealed class Topics : IKnownTopics
    {
        private readonly List<KnownTopic> topics = [];
        private TopicsSnapshot? snapshot;

        public Task<TopicsSnapshot?> SnapshotAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) => Task.FromResult(snapshot);

        public Task<IReadOnlyList<KnownTopic>> ListAsync(string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnownTopic>>([.. topics]);

        public Task<IReadOnlyList<Guid>> ActiveAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, int>> SizesAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> FoundationsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Add(TopicsSnapshot snapshot) => this.snapshot = snapshot;

        public void Add(KnownTopic topic) => topics.Add(topic);

        public void Remove(KnownTopic topic) => topics.Remove(topic);
    }

    private sealed class NoDoneBlocks : IDoneBlocks
    {
        public Task<IReadOnlyList<DoneBlock>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(DoneBlock block)
        {
        }
    }

    private sealed class Outbox : IEventOutbox
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public void Add(IIntegrationEvent integrationEvent) => Events.Add(integrationEvent);
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}