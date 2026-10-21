using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Sender.Contracts;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// One event from one service to another: two services in this process, each with its own database, joined only by the
// broker. The first three tests use a real NATS, the last one shows that the broker is replaceable.
[Collection(NatsCollection.Name)]
public sealed class CrossServiceTests(MySqlTestServer mysql, NatsTestServer nats) : IAsyncLifetime
{
    private readonly HandlerBehavior behavior = new();
    private string senderDatabase = "";
    private string receiverDatabase = "";
    private TestService<SenderDbContext>? sender;
    private TestService<ReceiverDbContext>? receiver;

    public async ValueTask InitializeAsync()
    {
        senderDatabase = await mysql.CreateDatabaseAsync("sender");
        receiverDatabase = await mysql.CreateDatabaseAsync("receiver");
        await NatsTransports.DeleteStreamAsync(nats.Url, "SENDER");
    }

    public async ValueTask DisposeAsync()
    {
        if (receiver is not null)
        {
            await receiver.DisposeAsync();
        }

        if (sender is not null)
        {
            await sender.DisposeAsync();
        }
    }

    [Fact]
    public async Task Event_GoesFromOneServiceToTheOther_OverNats()
    {
        await StartSenderAsync(natsUrl: nats.Url);
        await StartReceiverAsync(natsUrl: nats.Url);

        var greetingId = Greet("Hallo");

        await WaitForHandledAsync(greetingId);
        Assert.Equal(1, Notifications(greetingId));
        // The dispatcher marks the row a moment after the send, which the receiver may beat.
        await Wait.UntilAsync(
            () => sender!.Query(db => db.Set<OutboxMessage>().Single().SentAt != null),
            "The event was not marked as sent.",
            () => sender!.Logs);
        var message = sender!.Query(db => db.Set<OutboxMessage>().Single());
        Assert.Equal("sender.greeted", message.Subject);
        Assert.Equal(1, receiver!.Query(db => db.Set<InboxMessage>().Count(m => m.MessageId == message.MessageId)));
    }

    [Fact]
    public async Task EventSentWhileTheReceiverIsDown_ArrivesWhenItStarts()
    {
        await StartSenderAsync(natsUrl: nats.Url);

        var greetingId = Greet("Hallo");
        await Wait.UntilAsync(
            () => sender!.Query(db => db.Set<OutboxMessage>().All(m => m.SentAt != null)),
            "The event was not sent.",
            () => sender!.Logs);

        await StartReceiverAsync(natsUrl: nats.Url);

        await WaitForHandledAsync(greetingId);
        Assert.Equal(1, Notifications(greetingId));
    }

    // The effect is stored, then the confirmation gets lost and NATS delivers the event again. The inbox drops it.
    [Fact]
    public async Task EventDeliveredAgainByNats_HasOneEffect()
    {
        LosingAcks? losing = null;
        await StartSenderAsync(natsUrl: nats.Url);
        await StartReceiverAsync(
            natsUrl: nats.Url,
            configure: services => services.AddSingleton<IMessageTransport>(provider =>
                losing = new LosingAcks(ActivatorUtilities.CreateInstance<NatsTransport>(provider, nats.Url))));

        var greetingId = Greet("Hallo");
        var marker = Greet("Marker");

        await WaitForHandledAsync(marker);
        await WaitForHandledAsync(greetingId);
        // NATS delivers the first event a second time after a few seconds, which makes three deliveries in all.
        await Wait.UntilAsync(() => losing!.Deliveries >= 3, "NATS did not deliver the event again.", () => receiver!.Logs);
        Assert.Equal(1, Notifications(greetingId));
        // The handler ran for the first event and for the marker, the second delivery stopped at the inbox.
        Assert.Equal(2, behavior.Calls);
    }

    // The same flow without NATS, the services share a broker inside the process.
    [Fact]
    public async Task Event_GoesFromOneServiceToTheOther_WithTheBrokerReplaced()
    {
        var broker = new InMemoryTransport();
        await StartSenderAsync(configure: services => services.AddInMemoryTransport(broker));
        await StartReceiverAsync(configure: services => services.AddInMemoryTransport(broker));

        var greetingId = Greet("Hallo");

        await WaitForHandledAsync(greetingId);
        Assert.Equal(1, Notifications(greetingId));
        Assert.Equal("sender.greeted", Assert.Single(broker.Published).Subject);
    }

    private async Task StartSenderAsync(string? natsUrl = null, Action<IServiceCollection>? configure = null)
    {
        sender = TestService<SenderDbContext>.Create("sender", senderDatabase, natsUrl, configure);
        await sender.StartAsync();
    }

    private async Task StartReceiverAsync(string? natsUrl = null, Action<IServiceCollection>? configure = null)
    {
        receiver = TestService<ReceiverDbContext>.Create(
            "receiver",
            receiverDatabase,
            natsUrl,
            services =>
            {
                services.AddSingleton(behavior);
                services.AddIntegrationEventHandler<Greeted, GreetedHandler>();
                configure?.Invoke(services);
            });
        await receiver.StartAsync();
    }

    // A change in the sending service and its event, stored together. Returns the id the receiver will see.
    private Guid Greet(string text)
    {
        var greetingId = Guid.NewGuid();
        sender!.Save((db, outbox) =>
        {
            db.Documents.Add(new Document { Id = greetingId, Title = $"{text} {greetingId}" });
            outbox.Add(new Greeted(greetingId, text));
        });
        return greetingId;
    }

    private Task WaitForHandledAsync(Guid greetingId) =>
        Wait.UntilAsync(
            () => Notifications(greetingId) > 0,
            "The event did not arrive.",
            () => sender!.Logs,
            () => receiver!.Logs);

    private int Notifications(Guid greetingId) =>
        receiver!.Query(db => db.Notifications.Count(n => n.GreetingId == greetingId));
}

public static class NatsCollection
{
    public const string Name = "nats";
}