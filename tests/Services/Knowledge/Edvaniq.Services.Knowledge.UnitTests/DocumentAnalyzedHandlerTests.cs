using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Knowledge.UnitTests;

// The handler takes the topics over once per document, without a learning goal until #215, and announces the new state
// of the learning goal once.
public class DocumentAnalyzedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewDocument_AddsItsTopics_WithoutALearningGoal()
    {
        var topics = new FakeTopics();
        var analysis = new DocumentAnalyzed("alice", Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()])]);

        await Handler(topics, new FakeOutbox()).HandleAsync(analysis, TestContext.Current.CancellationToken);

        var import = Assert.Single(topics.Added);
        var topic = Assert.Single(import.Topics);
        Assert.Equal((DocumentAnalyzedHandler.NoLearningGoal, Now.UtcDateTime), (topic.LearningGoalId, topic.CreatedAt));
        Assert.Single(import.Sections);
        Assert.Equal(("alice", analysis.DocumentId), topics.AskedFor);
    }

    // The earlier topic of the goal is in the state too: the event carries all of them, the new ones among them.
    [Fact]
    public async Task NewDocument_AnnouncesTheWholeStateOfTheLearningGoal_Once()
    {
        var earlier = Guid.NewGuid();
        var topics = new FakeTopics { Stored = [earlier] };
        var outbox = new FakeOutbox();
        var analysis = new DocumentAnalyzed("alice", Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()]), new("Kern", 0, [])]);

        await Handler(topics, outbox).HandleAsync(analysis, TestContext.Current.CancellationToken);

        var changed = Assert.IsType<TopicsChanged>(Assert.Single(outbox.Added));
        Assert.Equal(("alice", DocumentAnalyzedHandler.NoLearningGoal, Now.UtcDateTime), (changed.UserId, changed.LearningGoalId, changed.ChangedAt));
        Assert.Equal([earlier, .. topics.Added[0].Topics.Select(topic => topic.Id)], changed.TopicIds);
        Assert.Empty(changed.HiddenTopicIds);
    }

    [Fact]
    public async Task DocumentWhoseTopicsAreThere_AddsAndAnnouncesNothing()
    {
        var topics = new FakeTopics { Exist = true };
        var outbox = new FakeOutbox();

        await Handler(topics, outbox).HandleAsync(new("alice", Guid.NewGuid(), [new("Zelle", null, [])]), TestContext.Current.CancellationToken);

        Assert.Empty(topics.Added);
        Assert.Empty(outbox.Added);
    }

    [Fact]
    public async Task AnalysisWithoutTopics_AnnouncesNothing()
    {
        var outbox = new FakeOutbox();

        await Handler(new FakeTopics(), outbox).HandleAsync(new("alice", Guid.NewGuid(), []), TestContext.Current.CancellationToken);

        Assert.Empty(outbox.Added);
    }

    [Fact]
    public async Task ChangedAt_KeepsWholeMicroseconds()
    {
        var outbox = new FakeOutbox();
        var time = new FixedTime(Now.AddTicks(1234567));

        await new TopicsChangedEvents(new FakeTopics(), outbox, time).AnnounceAsync("alice", Guid.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(Now.UtcDateTime.AddTicks(1234560), Assert.IsType<TopicsChanged>(Assert.Single(outbox.Added)).ChangedAt);
    }

    private static DocumentAnalyzedHandler Handler(FakeTopics topics, FakeOutbox outbox) =>
        new(topics, new TopicsChangedEvents(topics, outbox, new FixedTime(Now)), new FixedTime(Now));

    // Plays both the store of the handler and the state that sees what was added.
    private sealed class FakeTopics : IAnalyzedTopics, ITopicState
    {
        public bool Exist { get; init; }

        public List<Guid> Stored { get; init; } = [];

        public (string, Guid)? AskedFor { get; private set; }

        public List<TopicImport> Added { get; } = [];

        public Task<bool> ExistForAsync(string ownerId, Guid documentId, CancellationToken cancellationToken)
        {
            AskedFor = (ownerId, documentId);
            return Task.FromResult(Exist);
        }

        public void Add(TopicImport import) => Added.Add(import);

        public Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The handler never asks for this.");

        public Task<IReadOnlyList<Guid>> TopicIdsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([.. Stored, .. Added.SelectMany(import => import.Topics).Select(topic => topic.Id)]);
    }

    private sealed class FakeOutbox : IEventOutbox
    {
        public List<IIntegrationEvent> Added { get; } = [];

        public void Add(IIntegrationEvent integrationEvent) => Added.Add(integrationEvent);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}