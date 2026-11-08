using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;

namespace Edvaniq.Services.Knowledge.UnitTests;

// The handler takes the topics over once per document, without a learning goal until #215.
public class DocumentAnalyzedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewDocument_AddsItsTopics_WithoutALearningGoal()
    {
        var topics = new FakeTopics();
        var analysis = new DocumentAnalyzed("alice", Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()])]);

        await new DocumentAnalyzedHandler(topics, new FixedTime(Now)).HandleAsync(analysis, TestContext.Current.CancellationToken);

        var import = Assert.Single(topics.Added);
        var topic = Assert.Single(import.Topics);
        Assert.Equal((DocumentAnalyzedHandler.NoLearningGoal, Now.UtcDateTime), (topic.LearningGoalId, topic.CreatedAt));
        Assert.Single(import.Sections);
        Assert.Equal(("alice", analysis.DocumentId), topics.AskedFor);
    }

    [Fact]
    public async Task DocumentWhoseTopicsAreThere_AddsNothing()
    {
        var topics = new FakeTopics { Exist = true };

        await new DocumentAnalyzedHandler(topics, new FixedTime(Now)).HandleAsync(
            new("alice", Guid.NewGuid(), [new("Zelle", null, [])]),
            TestContext.Current.CancellationToken);

        Assert.Empty(topics.Added);
    }

    private sealed class FakeTopics : IAnalyzedTopics
    {
        public bool Exist { get; init; }

        public (string, Guid)? AskedFor { get; private set; }

        public List<TopicImport> Added { get; } = [];

        public Task<bool> ExistForAsync(string ownerId, Guid documentId, CancellationToken cancellationToken)
        {
            AskedFor = (ownerId, documentId);
            return Task.FromResult(Exist);
        }

        public void Add(TopicImport import) => Added.Add(import);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}