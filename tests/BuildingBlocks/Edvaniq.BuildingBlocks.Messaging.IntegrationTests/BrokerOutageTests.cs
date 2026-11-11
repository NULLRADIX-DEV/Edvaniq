using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Edvaniq.Services.Sender.Contracts;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

using Testcontainers.Nats;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// The broker goes away and comes back. Meanwhile the services keep storing their changes, the events wait in the outbox,
// and once the broker is back every one of them arrives exactly once. Each test has a broker of its own on a fixed port,
// so a restart keeps its address, as the server's does.
public sealed class BrokerOutageTests(MySqlTestServer mysql) : IAsyncLifetime
{
    // A request must not wait for the broker. Storing a change takes milliseconds, this leaves room for a slow runner.
    private static readonly TimeSpan SaveLimit = TimeSpan.FromSeconds(2);

    private readonly HandlerBehavior behavior = new();
    private readonly NatsContainer broker = new NatsBuilder(NatsTestServer.Image).WithPortBinding(FreePort(), 4222).Build();
    private string senderDatabase = "";
    private string receiverDatabase = "";
    private TestService<SenderDbContext>? sender;
    private TestService<ReceiverDbContext>? receiver;

    private string Url => broker.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        senderDatabase = await mysql.CreateDatabaseAsync("sender");
        receiverDatabase = await mysql.CreateDatabaseAsync("receiver");
        await broker.StartAsync(TestContext.Current.CancellationToken);
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

        await broker.DisposeAsync();
    }

    [Fact]
    public async Task StoppedBroker_ChangesAreStored_AndEveryEventArrivesOnceAfterTheRestart()
    {
        await StartSenderAsync();
        await StartReceiverAsync();
        var before = Greet("Vorher");
        await WaitForHandledAsync(before);

        await broker.StopAsync(TestContext.Current.CancellationToken);
        var during = Enumerable.Range(1, 3).Select(index => GreetQuickly($"Während {index}")).ToList();

        // The changes are there, their events wait, and the dispatcher has tried and failed at least once.
        Assert.Equal(during.Count, sender!.Query(db => db.Documents.Count(document => during.Contains(document.Id))));
        await Wait.UntilAsync(
            () => sender.Query(db => db.Set<OutboxMessage>().Any(message => message.SentAt == null && message.LastError != null)),
            "The dispatcher did not try to send while the broker was down.",
            () => sender.Logs);
        Assert.Equal(during.Count, sender.Query(db => db.Set<OutboxMessage>().Count(message => message.SentAt == null)));
        Assert.All(during, greeting => Assert.Equal(0, Notifications(greeting)));

        await broker.StartAsync(TestContext.Current.CancellationToken);

        foreach (var greeting in during)
        {
            await WaitForHandledAsync(greeting);
        }

        await WaitForOutboxSentAsync();
        Assert.All(during.Append(before), greeting => Assert.Equal(1, Notifications(greeting)));
    }

    // Sent before the outage but not yet handled: the stream keeps the event over the restart, and the receiver, started
    // while the broker was down, gets it once it is back.
    [Fact]
    public async Task EventSentBeforeTheOutage_ArrivesAtAReceiverThatStartedDuringIt()
    {
        await StartSenderAsync();
        var greeting = Greet("Vorher");
        await WaitForOutboxSentAsync();

        await broker.StopAsync(TestContext.Current.CancellationToken);
        await StartReceiverAsync();
        await broker.StartAsync(TestContext.Current.CancellationToken);

        await WaitForHandledAsync(greeting);
        Assert.Equal(1, Notifications(greeting));
    }

    // A broker that hangs instead of stopping: nothing waits on it either, and the events go out when it answers again.
    [Fact]
    public async Task HangingBroker_ChangesAreStored_AndTheEventsArriveAfterwards()
    {
        await StartSenderAsync();
        await StartReceiverAsync();
        var before = Greet("Vorher");
        await WaitForHandledAsync(before);

        await broker.PauseAsync(TestContext.Current.CancellationToken);
        var during = GreetQuickly("Während");
        Assert.Equal(1, sender!.Query(db => db.Documents.Count(document => document.Id == during)));
        await broker.UnpauseAsync(TestContext.Current.CancellationToken);

        await WaitForHandledAsync(during);
        await WaitForOutboxSentAsync();
        Assert.Equal(1, Notifications(during));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task StartSenderAsync()
    {
        sender = TestService<SenderDbContext>.Create("sender", senderDatabase, Url);
        await sender.StartAsync();
    }

    private async Task StartReceiverAsync()
    {
        receiver = TestService<ReceiverDbContext>.Create(
            "receiver",
            receiverDatabase,
            Url,
            services =>
            {
                services.AddSingleton(behavior);
                services.AddIntegrationEventHandler<Greeted, GreetedHandler>();
            });
        await receiver.StartAsync();
    }

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

    // A change while the broker is away, stored in no more time than with it.
    private Guid GreetQuickly(string text)
    {
        var watch = Stopwatch.StartNew();
        var greetingId = Greet(text);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, SaveLimit);
        return greetingId;
    }

    private Task WaitForHandledAsync(Guid greetingId) =>
        Wait.UntilAsync(
            () => Notifications(greetingId) > 0,
            "The event did not arrive.",
            () => sender!.Logs,
            () => receiver?.Logs ?? "");

    private Task WaitForOutboxSentAsync() =>
        Wait.UntilAsync(
            () => sender!.Query(db => db.Set<OutboxMessage>().All(message => message.SentAt != null)),
            "The outbox was not sent.",
            () => sender!.Logs);

    private int Notifications(Guid greetingId) =>
        receiver!.Query(db => db.Notifications.Count(notification => notification.GreetingId == greetingId));
}