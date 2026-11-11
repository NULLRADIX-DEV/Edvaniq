using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.UnitTests;

// The handler takes the topics over into the learning goal of the event, known ones only once, and announces the new state
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

    // A retry of the analysis (#237) sends the same document with sections cut anew.
    [Fact]
    public async Task SameDocumentAgain_WithNewSections_KeepsTheTopics_ReplacesTheSections_AndAnnouncesNothing()
    {
        var topics = new FakeTopics();
        var document = Guid.NewGuid();
        await Handler(topics, new FakeOutbox()).HandleAsync(
            new("alice", document, [new("Zelle", null, [Guid.NewGuid()]), new("Kern", 0, [Guid.NewGuid()])], Goal),
            TestContext.Current.CancellationToken);
        var first = topics.Added[0].Topics;
        var outbox = new FakeOutbox();
        var sections = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await Handler(topics, outbox).HandleAsync(
            new("alice", document, [new("Zelle", null, [sections[0]]), new("Kern", 0, [sections[1]])], Goal),
            TestContext.Current.CancellationToken);

        Assert.Empty(topics.Added[1].Topics);
        Assert.Equal(
            [(first[0].Id, sections[0]), (first[1].Id, sections[1])],
            topics.Sections[document].Select(section => (section.TopicId, section.SectionId)));
        Assert.Empty(outbox.Added);
    }

    [Fact]
    public async Task AnotherDocumentOnKnownTopicsOnly_AddsAndAnnouncesNothing()
    {
        var known = Topic.Create("alice", Goal, "Zelle", Now.UtcDateTime, Guid.NewGuid());
        var topics = new FakeTopics { Stored = [known] };
        var outbox = new FakeOutbox();
        var document = Guid.NewGuid();

        await Handler(topics, outbox).HandleAsync(new("alice", document, [new("zelle", null, [Guid.NewGuid()])], Goal), TestContext.Current.CancellationToken);

        Assert.Empty(Assert.Single(topics.Added).Topics);
        Assert.Equal(known.Id, Assert.Single(topics.Sections[document]).TopicId);
        Assert.Empty(outbox.Added);
    }

    // The moved topics count as known in the goal they joined.
    [Fact]
    public async Task FirstDocumentWithAGoal_TakesAMovedTopicOfTheSameName()
    {
        var old = Topic.Create("alice", DocumentAnalyzedHandler.NoLearningGoal, "Zelle", Now.UtcDateTime, Guid.NewGuid());
        var topics = new FakeTopics { Stored = [old] };

        await Handler(topics, new FakeOutbox()).HandleAsync(
            new("alice", Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()])], Goal), TestContext.Current.CancellationToken);

        Assert.Empty(Assert.Single(topics.Added).Topics);
        Assert.Equal(old.Id, Assert.Single(topics.Added[0].Sections).TopicId);
    }

    [Fact]
    public async Task AnalysisWithoutTopics_AnnouncesNothing()
    {
        var outbox = new FakeOutbox();

        await Handler(new FakeTopics(), outbox).HandleAsync(new("alice", Guid.NewGuid(), [], Goal), TestContext.Current.CancellationToken);

        Assert.Empty(outbox.Added);
    }

    // #909: every subtopic comes with its parent, new or known, so receivers can tell the foundations.
    [Fact]
    public async Task NewDocument_AnnouncesEverySubtopicWithItsParent()
    {
        var topics = new FakeTopics { Stored = [Topic.Create("alice", Goal, "Alt", Now.UtcDateTime)] };
        var outbox = new FakeOutbox();
        var analysis = new DocumentAnalyzed("alice", Guid.NewGuid(), [new("Zelle", null, []), new("Kern", 0, []), new("Membran", 0, [])], Goal);

        await Handler(topics, outbox).HandleAsync(analysis, TestContext.Current.CancellationToken);

        var added = topics.Added[0].Topics;
        Assert.Equal(
            [new TopicParent(added[1].Id, added[0].Id), new TopicParent(added[2].Id, added[0].Id)],
            Assert.IsType<TopicsChanged>(Assert.Single(outbox.Added)).Parents);
    }

    // A known topic the analysis merges a suggestion into keeps its place under its parent, and a new subtopic of it
    // names the known parent.
    [Fact]
    public async Task MergedTopics_KeepTheirParent()
    {
        var parent = Topic.Create("alice", Goal, "Zelle", Now.UtcDateTime);
        var child = parent.AddSubtopic("Kern", Now.UtcDateTime);
        var topics = new FakeTopics { Stored = [parent, child] };
        var outbox = new FakeOutbox();
        var analysis = new DocumentAnalyzed("alice", Guid.NewGuid(), [new("zelle", null, []), new("Kern", 0, []), new("Membran", 0, [])], Goal);

        await Handler(topics, outbox).HandleAsync(analysis, TestContext.Current.CancellationToken);

        var added = Assert.Single(topics.Added[0].Topics);
        Assert.Equal(
            [new TopicParent(child.Id, parent.Id), new TopicParent(added.Id, parent.Id)],
            Assert.IsType<TopicsChanged>(Assert.Single(outbox.Added)).Parents);
    }

    // Topics without a goal move with their parents, so the goal they join names them, the old goal names none.
    [Fact]
    public async Task MovedTopics_TakeTheirParentAlong()
    {
        var parent = Topic.Create("alice", DocumentAnalyzedHandler.NoLearningGoal, "Zelle", Now.UtcDateTime);
        var child = parent.AddSubtopic("Kern", Now.UtcDateTime);
        var topics = new FakeTopics { Stored = [parent, child] };
        var outbox = new FakeOutbox();

        await Handler(topics, outbox).HandleAsync(
            new("alice", Guid.NewGuid(), [new("Gewebe", null, [])], Goal), TestContext.Current.CancellationToken);

        var changes = outbox.Added.Cast<TopicsChanged>().ToList();
        Assert.Equal([new TopicParent(child.Id, parent.Id)], changes[0].Parents);
        Assert.Empty(changes[1].Parents!);
    }

    [Fact]
    public async Task ChangedAt_KeepsWholeMicroseconds()
    {
        var outbox = new FakeOutbox();
        var time = new FixedTime(Now.AddTicks(1234567));

        await new TopicsChangedEvents(new FakeTopics(), outbox, time).AnnounceAsync("alice", Guid.Empty, TestContext.Current.CancellationToken);

        Assert.Equal(Now.UtcDateTime.AddTicks(1234560), Assert.IsType<TopicsChanged>(Assert.Single(outbox.Added)).ChangedAt);
    }

    // DocumentDeleted came first on its own consumer: the analysis of the deleted document brings nothing back.
    [Fact]
    public async Task AnalysisOfADeletedDocument_AddsNothing_AndAnnouncesNothing()
    {
        var topics = new FakeTopics();
        var outbox = new FakeOutbox();
        var analyzed = new DocumentAnalyzed("alice", Guid.NewGuid(), [new TopicSuggestion("Photosynthese", null, [Guid.NewGuid()])], Guid.NewGuid());
        var deleted = new FakeDeleted();
        deleted.Documents.Add(("alice", analyzed.DocumentId));

        await Handler(topics, outbox, deleted).HandleAsync(analyzed, TestContext.Current.CancellationToken);

        Assert.Empty(topics.Added);
        Assert.Empty(outbox.Added);
    }

    private static DocumentAnalyzedHandler Handler(FakeTopics topics, FakeOutbox outbox, FakeDeleted? deleted = null) =>
        new(topics, deleted ?? new FakeDeleted(), new TopicsChangedEvents(topics, outbox, new FixedTime(Now)), new FixedTime(Now));

    private sealed class FakeDeleted : IDeletedDocuments
    {
        public HashSet<(string OwnerId, Guid DocumentId)> Documents { get; } = [];

        public Task<bool> IsDeletedAsync(string ownerId, Guid documentId, CancellationToken cancellationToken) =>
            Task.FromResult(Documents.Contains((ownerId, documentId)));

        public Task<IReadOnlyList<Topic>> RemoveAsync(string ownerId, Guid documentId, DateTime deletedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The handler never removes.");
    }

    // Plays both the store of the handler and the state that sees what was added or moved. Added topics count as stored
    // for the next handler, as after a save.
    private sealed class FakeTopics : IAnalyzedTopics, ITopicState
    {
        public List<Topic> Stored { get; init; } = [];

        public List<TopicImport> Added { get; } = [];

        // The sections of each document, replaced by every import of it.
        public Dictionary<Guid, IReadOnlyList<TopicSection>> Sections { get; } = [];

        private IEnumerable<Topic> All => Stored.Concat(Added.SelectMany(import => import.Topics));

        public Task ImportAsync(TopicImport import, CancellationToken cancellationToken)
        {
            Added.Add(import);
            Sections[import.DocumentId] = import.Sections;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Topic>> OfGoalAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Topic>>([.. All.Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId)]);

        public Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The handler never asks for this.");

        public Task<IReadOnlyList<TopicInState>> TopicsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TopicInState>>(
                [.. All.Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId).Select(topic => new TopicInState(topic.Id, topic.ParentId))]);
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