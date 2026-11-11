using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.Planning.Contracts;

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

        await new SessionCompletedHandler(introductions).HandleAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal([first, second], introductions.Added.Select(item => item.TopicId));
        Assert.All(introductions.Added, item =>
            Assert.Equal(("alice", session.SessionId, EndedAt), (item.OwnerId, item.SessionId, item.IntroducedAt)));
        Assert.Equal(("alice", 2), (introductions.AskedFor, introductions.AskedTopics));
    }

    // The same session again, or a later one, doesn't introduce a topic a second time.
    [Fact]
    public async Task TopicIntroducedBefore_KeepsItsFirstIntroduction()
    {
        var (known, fresh) = (Guid.NewGuid(), Guid.NewGuid());
        var introductions = new FakeIntroductions { Known = [known] };

        await new SessionCompletedHandler(introductions).HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, [known, fresh]), TestContext.Current.CancellationToken);

        Assert.Equal(fresh, Assert.Single(introductions.Added).TopicId);
    }

    // A broken-off session still names the units finished in it.
    [Fact]
    public async Task BrokenOffSession_StillIntroducesItsTopics()
    {
        var introductions = new FakeIntroductions();

        await new SessionCompletedHandler(introductions).HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, false, [Guid.NewGuid()]), TestContext.Current.CancellationToken);

        Assert.Single(introductions.Added);
    }

    [Fact]
    public async Task SessionWithoutIntroductions_ChangesNothing()
    {
        var introductions = new FakeIntroductions();

        await new SessionCompletedHandler(introductions).HandleAsync(
            new SessionCompleted("alice", Guid.NewGuid(), EndedAt, true, []), TestContext.Current.CancellationToken);

        Assert.Empty(introductions.Added);
        Assert.Null(introductions.AskedFor);
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