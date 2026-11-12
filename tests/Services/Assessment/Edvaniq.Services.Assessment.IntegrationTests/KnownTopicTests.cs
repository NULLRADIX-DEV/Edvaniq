using System.Text.Json;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Knowledge.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// TopicsChanged from Knowledge leaves the ids of the newest state in the database, whatever arrives twice or late, and
// UserDeleted takes them with the rest of the user's data.
public sealed class KnownTopicTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-TopicsChangedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly DateTime First = new(2026, 11, 11, 9, 0, 0, 123, 456, DateTimeKind.Utc);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task ChangedTopics_AreKnown_WithTheirStateStored()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (a, b, hidden) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await DeliverAsync(new TopicsChanged(alice, goal, First, [a, b], [hidden]));

        Assert.Equal(new[] { (a, false), (b, false), (hidden, true) }.Order(), Known(alice).Order());
        Assert.Equal(First, Query(db => db.TopicsSnapshots.IgnoreQueryFilters().Single(snapshot => snapshot.OwnerId == alice).ChangedAt));
    }

    // A redelivery is dropped by the inbox, the same state under a new id and an older state by the stored snapshot.
    [Fact]
    public async Task StateDeliveredTwice_SentTwice_OrOlder_ChangesNothing()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (kept, added) = (Guid.NewGuid(), Guid.NewGuid());
        await DeliverAsync(new TopicsChanged(alice, goal, First, [kept], []));
        var newer = new TopicsChanged(alice, goal, First.AddSeconds(1), [kept, added], []);
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(newer, messageId);
            await PublishAsync(newer, messageId);
            await PublishAsync(newer);
            var older = await PublishAsync(new TopicsChanged(alice, goal, First, [Guid.NewGuid()], []));

            // One consumer, in order: once the older state is through, so is everything before it.
            await WaitForAsync(() => InboxEntries(older) == 1, "The older state was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Equal(new[] { (kept, false), (added, false) }.Order(), Known(alice).Order());
            Assert.Equal(First.AddSeconds(1), Query(db => db.TopicsSnapshots.IgnoreQueryFilters().Single(snapshot => snapshot.OwnerId == alice).ChangedAt));
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    [Fact]
    public async Task DeletedUser_LosesTheKnownTopics_OthersKeepTheirs()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await DeliverAsync(new TopicsChanged(alice, Guid.NewGuid(), First, [Guid.NewGuid()], []));
        await DeliverAsync(new TopicsChanged(bob, Guid.NewGuid(), First, [Guid.NewGuid()], []));

        var deletion = await PublishAsync(new UserDeleted(alice));

        await WaitForAsync(
            () => Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == deletion)) == 1,
            "The deletion was not handled.");
        Assert.Empty(Known(alice));
        Assert.Equal(0, Query(db => db.TopicsSnapshots.IgnoreQueryFilters().Count(snapshot => snapshot.OwnerId == alice)));
        Assert.Single(Known(bob));
        Assert.Equal(1, Query(db => db.TopicsSnapshots.IgnoreQueryFilters().Count(snapshot => snapshot.OwnerId == bob)));
    }

    // #944: a state that comes after the user's deletion is confirmed and stores nothing.
    [Fact]
    public async Task LateState_OfADeletedUser_IsConfirmedWithoutEffect()
    {
        var alice = NewUser("alice");
        var deletion = await PublishAsync(new UserDeleted(alice));
        await WaitForAsync(
            () => Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == deletion)) == 1,
            "The deletion was not handled.");

        var late = await PublishAsync(new TopicsChanged(alice, Guid.NewGuid(), First, [Guid.NewGuid()], []));

        await WaitForAsync(() => InboxEntries(late) == 1, "The late state was not confirmed.");
        Assert.Empty(Known(alice));
        Assert.Equal(0, Query(db => db.TopicsSnapshots.IgnoreQueryFilters().Count(snapshot => snapshot.OwnerId == alice)));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private async Task DeliverAsync(TopicsChanged changed)
    {
        var messageId = await PublishAsync(changed);
        await WaitForAsync(() => InboxEntries(messageId) == 1, "The state was not handled.");
    }

    // Like the dispatcher of the sender would.
    private async Task<Guid> PublishAsync<TEvent>(TEvent integrationEvent, Guid? messageId = null)
        where TEvent : IIntegrationEvent
    {
        var id = messageId ?? Guid.NewGuid();
        var payload = JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(TEvent)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private List<(Guid TopicId, bool Hidden)> Known(string user) =>
        Query(db => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => new { topic.TopicId, topic.Hidden }).ToList())
            .Select(topic => (topic.TopicId, topic.Hidden))
            .ToList();

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<AssessmentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AssessmentDbContext>());
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