using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Testing;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// Each topic a session introduced gets one introduction, a topic introduced before keeps its first one.
public sealed class SessionCompletedHandlerTests
{
    private static readonly DateTime EndedAt = new(2026, 11, 9, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task IntroducedTopics_EachGetOneIntroduction()
    {
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var session = new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, [first, second, first]);
        var introductions = new FakeIntroductions();

        await Handler(introductions).HandleAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal([first, second], introductions.Added.Select(item => item.TopicId));
        Assert.All(introductions.Added, item =>
            Assert.Equal(("alice", session.SessionId, EndedAt), (item.OwnerId, item.SessionId, item.IntroducedAt)));
        Assert.Equal(("alice", 2), (introductions.AskedFor, introductions.AskedTopics));
    }

    // #1040: the introduction keeps when the unit started, an event from before without it leaves it empty.
    [Fact]
    public async Task UnitStart_GoesIntoTheIntroduction()
    {
        var (withStart, withoutStart) = (Guid.NewGuid(), Guid.NewGuid());
        var introductions = new FakeIntroductions();
        var handler = Handler(introductions);

        await handler.HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, [withStart], EndedAt.AddMinutes(-30)), TestContext.Current.CancellationToken);
        await handler.HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, [withoutStart]), TestContext.Current.CancellationToken);

        Assert.Equal(EndedAt.AddMinutes(-30), introductions.Added[0].UnitStartedAt);
        Assert.Null(introductions.Added[1].UnitStartedAt);
    }

    // The same session again, or a later one, doesn't introduce a topic a second time.
    [Fact]
    public async Task TopicIntroducedBefore_KeepsItsFirstIntroduction()
    {
        var (known, fresh) = (Guid.NewGuid(), Guid.NewGuid());
        var introductions = new FakeIntroductions { Known = [known] };

        await Handler(introductions).HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, [known, fresh]), TestContext.Current.CancellationToken);

        Assert.Equal(fresh, Assert.Single(introductions.Added).TopicId);
    }

    // A broken-off session still names the units finished in it.
    [Fact]
    public async Task BrokenOffSession_StillIntroducesItsTopics()
    {
        var introductions = new FakeIntroductions();

        await Handler(introductions).HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, false, [Guid.NewGuid()]), TestContext.Current.CancellationToken);

        Assert.Single(introductions.Added);
    }

    [Fact]
    public async Task SessionWithoutIntroductions_ChangesNothing()
    {
        var introductions = new FakeIntroductions();

        await Handler(introductions).HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, []), TestContext.Current.CancellationToken);

        Assert.Empty(introductions.Added);
        Assert.Null(introductions.AskedFor);
    }

    // #417: the session and how many topics it introduced, a topic not known yet on its own line. Ids only, never the
    // user.
    [Fact]
    public async Task Introductions_AreLogged_WithTheSession_AndTopicsNotKnownYet()
    {
        var (known, unknown) = (Guid.NewGuid(), Guid.NewGuid());
        var user = $"alice-{Guid.NewGuid():N}";
        var session = new SessionCompleted(user, Guid.NewGuid(), EndedAt, true, [known, unknown]);

        await Handler(new FakeIntroductions(), [known]).HandleAsync(session, TestContext.Current.CancellationToken);

        var introduced = Assert.Single(logs.Entries, entry => entry.EventName == "SessionIntroducedTopics");
        Assert.Equal(LogLevel.Information, introduced.Level);
        Assert.Contains(session.SessionId.ToString(), introduced.Message, StringComparison.Ordinal);
        Assert.Contains("2 topics", introduced.Message, StringComparison.Ordinal);
        var notKnown = Assert.Single(logs.Entries, entry => entry.EventName == "IntroducedTopicNotKnownYet");
        Assert.Contains(unknown.ToString(), notKnown.Message, StringComparison.Ordinal);
        Assert.Contains(session.SessionId.ToString(), notKnown.Message, StringComparison.Ordinal);
        Assert.Empty(logs.Containing(user));
    }

    // Nothing new introduced, nothing to say.
    [Fact]
    public async Task NoNewIntroduction_LogsNothing()
    {
        var topic = Guid.NewGuid();

        await Handler(new FakeIntroductions { Known = [topic] }, [topic]).HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, [topic]), TestContext.Current.CancellationToken);

        Assert.Empty(logs.Entries);
    }

    private readonly TestLogs logs = new();

    private SessionCompletedHandler Handler(FakeIntroductions introductions, IReadOnlyCollection<Guid>? knownTopics = null)
    {
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        return new SessionCompletedHandler(introductions, new FakeKnownTopics(knownTopics ?? []), factory.CreateLogger<SessionCompletedHandler>());
    }

    private sealed class FakeKnownTopics(IReadOnlyCollection<Guid> known) : IKnownTopics
    {
        public Task<IReadOnlySet<Guid>> KnownAsync(string ownerId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(known.Intersect(topicIds).ToHashSet());

        public Task<TopicsSnapshot?> SnapshotAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KnownTopic>> ListAsync(
            string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(TopicsSnapshot snapshot) => throw new NotSupportedException();

        public void Add(KnownTopic topic) => throw new NotSupportedException();

        public void Remove(KnownTopic topic) => throw new NotSupportedException();
    }

    private sealed class FakeIntroductions : ITopicIntroductions
    {
        public HashSet<Guid> Known { get; init; } = [];

        public string? AskedFor { get; private set; }

        public int AskedTopics { get; private set; }

        public List<TopicIntroduction> Added { get; } = [];

        public Task<IReadOnlySet<Guid>> IntroducedAsync(
            string ownerId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken)
        {
            (AskedFor, AskedTopics) = (ownerId, topicIds.Count);
            return Task.FromResult<IReadOnlySet<Guid>>(Known.Intersect(topicIds).ToHashSet());
        }

        public void Add(TopicIntroduction introduction) => Added.Add(introduction);
    }
}