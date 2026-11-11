using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.UnitTests;

// The handler takes the topics over once per document into the learning goal of the event and announces the new state
// of the goal once. The first analysis with a goal moves the user's topics without one there and announces both goals.
public class DocumentAnalyzedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Goal = Guid.NewGuid();

    [Fact]
    public async Task NewDocument_AddsItsTopics_ToTheLearningGoal()
    {
        var topics = new FakeTopics();
        var analysis = new DocumentAnalyzed("alice", Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()])], Goal);

        await Handler(topics, new FakeOutbox()).HandleAsync(analysis, TestContext.Current.CancellationToken);

        var import = Assert.Single(topics.Added);
        var topic = Assert.Single(import.Topics);
        Assert.Equal((Goal, Now.UtcDateTime), (topic.LearningGoalId, topic.CreatedAt));
        Assert.Single(import.Sections);
        Assert.Equal(("alice", analysis.DocumentId), topics.AskedFor);
    }

    // A message from before the default goal still finds a place.
    [Fact]
    public async Task DocumentWithoutAGoal_AddsItsTopics_WithoutALearningGoal()
    {
        var topics = new FakeTopics();

        await Handler(topics, new FakeOutbox()).HandleAsync(
            new("alice", Guid.NewGuid(), [new("Zelle", null, [])]), TestContext.Current.CancellationToken);

        Assert.Equal(DocumentAnalyzedHandler.NoLearningGoal, Assert.Single(Assert.Single(topics.Added).Topics).LearningGoalId);
    }

    // The earlier topic of the goal is in the state too: the event carries all of them, the new ones among them.
    [Fact]
    public async Task NewDocument_AnnouncesTheWholeStateOfTheLearningGoal_Once()
    {
        var earlier = Topic.Create("alice", Goal, "Alt", Now.UtcDateTime);
        var topics = new FakeTopics { Stored = [earlier] };
        var outbox = new FakeOutbox();
        var analysis = new DocumentAnalyzed("alice", Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()]), new("Kern", 0, [])], Goal);

        await Handler(topics, outbox).HandleAsync(analysis, TestContext.Current.CancellationToken);

        var changed = Assert.IsType<TopicsChanged>(Assert.Single(outbox.Added));
        Assert.Equal(("alice", Goal, Now.UtcDateTime), (changed.UserId, changed.LearningGoalId, changed.ChangedAt));
        Assert.Equal([earlier.Id, .. topics.Added[0].Topics.Select(topic => topic.Id)], changed.TopicIds);
        Assert.Empty(changed.HiddenTopicIds);
    }

    // The topics keep their ids, so questions and records stay with them. Topics of other users stay where they are.
    [Fact]
    public async Task FirstDocumentWithAGoal_MovesTheTopicsWithoutOne_AndAnnouncesBothGoals()
    {
        var old = Topic.Create("alice", DocumentAnalyzedHandler.NoLearningGoal, "Alt", Now.UtcDateTime);
        var bobs = Topic.Create("bob", DocumentAnalyzedHandler.NoLearningGoal, "Bobs", Now.UtcDateTime);
        var topics = new FakeTopics { Stored = [old, bobs] };
        var outbox = new FakeOutbox();

        await Handler(topics, outbox).HandleAsync(
            new("alice", Guid.NewGuid(), [new("Zelle", null, [])], Goal), TestContext.Current.CancellationToken);

        Assert.Equal((Goal, DocumentAnalyzedHandler.NoLearningGoal), (old.LearningGoalId, bobs.LearningGoalId));
        var changes = outbox.Added.Cast<TopicsChanged>().ToList();
        Assert.Equal([Goal, DocumentAnalyzedHandler.NoLearningGoal], changes.Select(change => change.LearningGoalId));
        Assert.Equal([old.Id, Assert.Single(topics.Added[0].Topics).Id], changes[0].TopicIds);
        Assert.Empty(changes[1].TopicIds);
        Assert.All(changes, change => Assert.Equal("alice", change.UserId));
    }

    [Fact]
    public async Task SecondDocumentWithAGoal_MovesNothing_AndAnnouncesOnlyItsGoal()
    {
        var topics = new FakeTopics { Stored = [Topic.Create("alice", DocumentAnalyzedHandler.NoLearningGoal, "Alt", Now.UtcDateTime)] };

        await Handler(topics, new FakeOutbox()).HandleAsync(new("alice", Guid.NewGuid(), [new("Zelle", null, [])], Goal), TestContext.Current.CancellationToken);
        var outbox = new FakeOutbox();

        await Handler(topics, outbox).HandleAsync(
            new("alice", Guid.NewGuid(), [new("Kern", null, [])], Goal), TestContext.Current.CancellationToken);

        var changed = Assert.IsType<TopicsChanged>(Assert.Single(outbox.Added));
        Assert.Equal(Goal, changed.LearningGoalId);
        Assert.Equal(3, changed.TopicIds.Count);
    }

    [Fact]
    public async Task DocumentWhoseTopicsAreThere_AddsMovesAndAnnouncesNothing()
    {
        var old = Topic.Create("alice", DocumentAnalyzedHandler.NoLearningGoal, "Alt", Now.UtcDateTime);
        var topics = new FakeTopics { Exist = true, Stored = [old] };
        var outbox = new FakeOutbox();

        await Handler(topics, outbox).HandleAsync(new("alice", Guid.NewGuid(), [new("Zelle", null, [])], Goal), TestContext.Current.CancellationToken);

        Assert.Empty(topics.Added);
        Assert.Empty(outbox.Added);
        Assert.Equal(DocumentAnalyzedHandler.NoLearningGoal, old.LearningGoalId);
    }

    [Fact]
    public async Task AnalysisWithoutTopics_AnnouncesNothing()
    {
        var outbox = new FakeOutbox();

        await Handler(new FakeTopics(), outbox).HandleAsync(new("alice", Guid.NewGuid(), [], Goal), TestContext.Current.CancellationToken);

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

    // Plays both the store of the handler and the state that sees what was added or moved. Added topics count as stored
    // for the next handler, as after a save.
    private sealed class FakeTopics : IAnalyzedTopics, ITopicState
    {
        public bool Exist { get; init; }

        public List<Topic> Stored { get; init; } = [];

        public (string, Guid)? AskedFor { get; private set; }

        public List<TopicImport> Added { get; } = [];

        private IEnumerable<Topic> All => Stored.Concat(Added.SelectMany(import => import.Topics));

        public Task<bool> ExistForAsync(string ownerId, Guid documentId, CancellationToken cancellationToken)
        {
            AskedFor = (ownerId, documentId);
            return Task.FromResult(Exist);
        }

        public void Add(TopicImport import) => Added.Add(import);

        public Task<IReadOnlyList<Topic>> OfGoalAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Topic>>([.. All.Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId)]);

        public Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The handler never asks for this.");

        public Task<IReadOnlyList<Guid>> TopicIdsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                [.. All.Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId).Select(topic => topic.Id)]);
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