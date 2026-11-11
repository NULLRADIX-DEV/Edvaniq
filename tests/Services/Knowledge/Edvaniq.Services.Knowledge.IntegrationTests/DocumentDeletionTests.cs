using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// DocumentDeleted takes the document's sections away, and the topics left without any. A topic another document brought
// too stays with that document's sections. Other users keep everything, a second delivery changes nothing, and an
// analysis of the deleted document that comes late brings nothing back.
public sealed class DocumentDeletionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-DocumentDeletedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly string Changed = EventRoute.For(typeof(TopicsChanged)).Subject;

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task DeletedDocument_TakesItsSectionsAndItsOwnTopics_TheSharedTopicStays()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var deleted = Analysis(alice, goal, ("Photosynthese", null), ("Lichtreaktion", 0), ("Zellatmung", null));
        var kept = Analysis(alice, goal, ("Zellatmung", null), ("Genetik", null));
        await DeliverAsync(deleted);
        await DeliverAsync(kept);
        var before = Events(alice).Count;

        await DeliverAsync(new DocumentDeleted(alice, deleted.DocumentId, goal));

        Assert.Equal(["Genetik", "Zellatmung"], Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice).Select(topic => topic.Name).OrderBy(name => name).ToList()));
        Assert.Equal(0, Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.DocumentId == deleted.DocumentId)));
        Assert.Equal(2, Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.DocumentId == kept.DocumentId)));
        var changes = Events(alice);
        Assert.Equal(before + 1, changes.Count);
        Assert.Equal(goal, changes[^1].LearningGoalId);
        Assert.Equal(2, changes[^1].TopicIds.Count);
    }

    [Fact]
    public async Task OtherUsersAndDocuments_KeepTheirTopics()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var deleted = Analysis(alice, Guid.NewGuid(), ("Photosynthese", null));
        var other = Analysis(alice, Guid.NewGuid(), ("Algebra", null));
        var ofBob = Analysis(bob, Guid.NewGuid(), ("Photosynthese", null)) with { DocumentId = deleted.DocumentId };
        await DeliverAsync(deleted);
        await DeliverAsync(other);
        await DeliverAsync(ofBob);

        await DeliverAsync(new DocumentDeleted(alice, deleted.DocumentId, deleted.LearningGoalId));

        Assert.Equal((1, 1), Rows(alice));
        Assert.Equal((1, 1), Rows(bob));
    }

    // The same message twice, and the same event under a new id: the state and the topics announced stay as after the first.
    [Fact]
    public async Task DeletionDeliveredTwice_OrSentTwice_ChangesNothingMore()
    {
        var alice = NewUser("alice");
        var analysis = Analysis(alice, Guid.NewGuid(), ("Photosynthese", null), ("Lichtreaktion", 0));
        await DeliverAsync(analysis);
        var deletion = new DocumentDeleted(alice, analysis.DocumentId, analysis.LearningGoalId);
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(deletion, messageId);
            await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
            var announced = Events(alice).Count;
            await PublishAsync(deletion, messageId);
            var again = await PublishAsync(deletion);

            await WaitForAsync(() => InboxEntries(again) == 1, "The second deletion was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Equal((0, 0), Rows(alice));
            Assert.Equal(announced, Events(alice).Count);
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // DocumentDeleted overtook the analysis on its own consumer.
    [Fact]
    public async Task AnalysisAfterTheDeletion_BringsNothingBack()
    {
        var alice = NewUser("alice");
        var analysis = Analysis(alice, Guid.NewGuid(), ("Photosynthese", null));

        await DeliverAsync(new DocumentDeleted(alice, analysis.DocumentId, analysis.LearningGoalId));
        await DeliverAsync(analysis);

        Assert.Equal((0, 0), Rows(alice));
        Assert.Empty(Events(alice));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    // One section per topic, a parent is the index of an earlier topic.
    private static DocumentAnalyzed Analysis(string user, Guid goal, params (string Name, int? Parent)[] topics) =>
        new(user, Guid.NewGuid(), [.. topics.Select(topic => new TopicSuggestion(topic.Name, topic.Parent, [Guid.NewGuid()]))], goal);

    private async Task DeliverAsync<T>(T integrationEvent)
    {
        var messageId = await PublishAsync(integrationEvent);
        await WaitForAsync(() => Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId)) == 1, $"The {typeof(T).Name} was not handled.");
    }

    // Like Content's dispatcher would.
    private async Task<Guid> PublishAsync<T>(T integrationEvent, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        var payload = JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(T)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private List<TopicsChanged> Events(string user) =>
        [.. Query(db => db.Set<OutboxMessage>().Where(message => message.Subject == Changed).OrderBy(message => message.Id).ToList())
            .Select(message => JsonSerializer.Deserialize<TopicsChanged>(message.Payload, JsonSerializerOptions.Web)!)
            .Where(changed => changed.UserId == user)];

    private (int Topics, int Sections) Rows(string user) =>
        (Query(db => db.Topics.IgnoreQueryFilters().Count(topic => topic.OwnerId == user)),
         Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.OwnerId == user)));

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<KnowledgeDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>());
    }

    // The consumer runs in the background.
    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}