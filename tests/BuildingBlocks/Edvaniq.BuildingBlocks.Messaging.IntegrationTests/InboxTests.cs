using System.Diagnostics;
using System.Text.Json;

using Edvaniq.Services.Sender.Contracts;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// The consumer against a real MySQL: the handler's changes and the inbox entry are stored together, a message the inbox
// knows has no second effect. The messages come from the broker inside the process, published like the dispatcher does.
public sealed class InboxTests(MySqlTestServer mysql) : IAsyncLifetime
{
    private const string Consumer = "receiver-GreetedHandler";

    private readonly InMemoryTransport transport = new() { RedeliveryDelay = TimeSpan.FromMilliseconds(20) };
    private readonly HandlerBehavior behavior = new();
    private string connectionString = "";
    private TestService<ReceiverDbContext>? receiver;

    public async ValueTask InitializeAsync() => connectionString = await mysql.CreateDatabaseAsync("inbox");

    public async ValueTask DisposeAsync()
    {
        if (receiver is not null)
        {
            await receiver.DisposeAsync();
        }
    }

    [Fact]
    public async Task Event_IsHandled_AndItsInboxEntryIsStoredWithTheEffect()
    {
        await StartReceiverAsync();

        var messageId = await PublishAsync("Hallo");

        await Wait.UntilAsync(() => Inbox(messageId, Consumer), "The event was not handled.", () => receiver!.Logs);
        Assert.Equal(1, Notifications());
    }

    [Fact]
    public async Task EventPublishedBeforeTheConsumerStarts_IsHandledWhenItStarts()
    {
        var messageId = await PublishAsync("Hallo");

        await StartReceiverAsync();

        await Wait.UntilAsync(() => Inbox(messageId, Consumer), "The event was not handled.", () => receiver!.Logs);
    }

    [Fact]
    public async Task EventDeliveredTwice_HasOneEffect()
    {
        transport.DropsDuplicates = false;
        await StartReceiverAsync();
        var messageId = Guid.NewGuid();
        var greetingId = Guid.NewGuid();

        await PublishAsync("Hallo", messageId, greetingId);
        await PublishAsync("Hallo", messageId, greetingId);
        var marker = await PublishAsync("Marker");

        // One consumer gets them in order, so the duplicate has been through before the marker is.
        await Wait.UntilAsync(() => Inbox(marker, Consumer), "The marker was not handled.", () => receiver!.Logs);
        Assert.Equal(1, Notifications(greetingId));
        Assert.Equal(2, receiver!.Query(db => db.Set<InboxMessage>().Count()));
        Assert.Equal(2, behavior.Calls);
    }

    // The handler changes the DbContext and then fails: nothing of it is stored, the event comes again and works.
    [Fact]
    public async Task HandlerThatFailsHalfway_StoresNothing_AndGetsTheEventAgain()
    {
        behavior.FailFirstCall = true;
        await StartReceiverAsync();
        var messageId = Guid.NewGuid();
        var greetingId = Guid.NewGuid();

        await PublishAsync("Hallo", messageId, greetingId);

        await Wait.UntilAsync(() => behavior.Calls >= 2 && Inbox(messageId, Consumer), "The event was not delivered again.", () => receiver!.Logs);
        Assert.Equal(2, behavior.Calls);
        Assert.Equal(1, Notifications(greetingId));
        Assert.Equal(1, receiver!.Query(db => db.Set<InboxMessage>().Count()));
    }

    // The effect is committed, then the process dies before it confirms the event. The inbox stops the second effect.
    [Fact]
    public async Task EventDeliveredAgainAfterALostConfirmation_HasOneEffect()
    {
        await StartReceiverAsync(configure: services => services.AddSingleton<IMessageTransport>(new LosingAcks(transport)));
        var messageId = Guid.NewGuid();
        var greetingId = Guid.NewGuid();

        await PublishAsync("Hallo", messageId, greetingId);
        var marker = await PublishAsync("Marker");

        await Wait.UntilAsync(() => Inbox(marker, Consumer), "The marker was not handled.", () => receiver!.Logs);
        Assert.Equal(1, Notifications(greetingId));
        Assert.Equal(1, receiver!.Query(db => db.Set<InboxMessage>().Count(m => m.MessageId == messageId)));
        // The handler ran once for the first event and once for the marker, the second delivery never reached it.
        Assert.Equal(2, behavior.Calls);
    }

    // The handler runs in the trace of the request that stored the event, and the log says when its effect is stored.
    [Fact]
    public async Task Event_IsHandledInTheTraceOfTheSender_AndTheLogSaysWhenItsEffectIsStored()
    {
        await StartReceiverAsync();
        var traceId = ActivityTraceId.CreateRandom();

        var messageId = await PublishAsync("Hallo", traceParent: $"00-{traceId.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        await Wait.UntilAsync(
            () => receiver!.Logs.Contains($"Consumer {Consumer} has handled message {messageId}", StringComparison.Ordinal),
            "The log does not say that the event was handled.",
            () => receiver!.Logs);
        Assert.True(Inbox(messageId, Consumer));
        Assert.Equal(traceId, Assert.Single(behavior.TraceIds));
    }

    [Fact]
    public async Task TwoHandlersOfOneEvent_EachHandleItOnce()
    {
        await StartReceiverAsync(configure: services => services.AddIntegrationEventHandler<Greeted, SecondGreetedHandler>());

        var messageId = await PublishAsync("Hallo");

        await Wait.UntilAsync(
            () => Inbox(messageId, Consumer) && Inbox(messageId, "receiver-SecondGreetedHandler"),
            "Not both handlers handled the event.",
            () => receiver!.Logs);
        Assert.Equal(["Hallo", "zweiter: Hallo"], receiver!.Query(db => db.Notifications.Select(n => n.Text).OrderBy(text => text).ToList()));
    }

    private async Task StartReceiverAsync(Action<IServiceCollection>? configure = null)
    {
        receiver = TestService<ReceiverDbContext>.Create(
            "receiver",
            connectionString,
            configure: services =>
            {
                services.AddInMemoryTransport(transport);
                services.AddSingleton(behavior);
                services.AddIntegrationEventHandler<Greeted, GreetedHandler>();
                configure?.Invoke(services);
            });
        await receiver.StartAsync();
    }

    // The way the dispatcher of the sending service publishes: the JSON of the event, under the MessageId.
    private async Task<Guid> PublishAsync(string text, Guid? messageId = null, Guid? greetingId = null, string? traceParent = null)
    {
        var id = messageId ?? Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new Greeted(greetingId ?? Guid.NewGuid(), text), MessageJson.Options);
        await transport.PublishAsync(new OutgoingMessage("sender.greeted", id, payload, traceParent), TestContext.Current.CancellationToken);
        return id;
    }

    private bool Inbox(Guid messageId, string consumer) =>
        receiver!.Query(db => db.Set<InboxMessage>().Any(m => m.MessageId == messageId && m.Consumer == consumer));

    private int Notifications(Guid? greetingId = null) =>
        receiver!.Query(db => db.Notifications.Count(n => greetingId == null || n.GreetingId == greetingId));
}