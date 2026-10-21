using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Spikes.Broker.Consumer;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;

using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Edvaniq.Spikes.Broker;

// The test publishes like the outbox dispatcher, the consumer runs as a second process.
public sealed class BrokerTests(NatsTestServer natsServer, MySqlTestServer mysql) : IAsyncLifetime
{
    private static int streamCount;

    private readonly NatsClient nats = new(natsServer.Url);
    private readonly string stream = $"DOCUMENTS{Interlocked.Increment(ref streamCount)}";
    private string connectionString = "";

    private string Subject => $"{stream.ToLowerInvariant()}.created";

    public async ValueTask InitializeAsync()
    {
        connectionString = await mysql.CreateDatabaseAsync("inbox");
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
        }

        await nats.CreateJetStreamContext().CreateStreamAsync(new StreamConfig(stream, [Subject]));
    }

    public ValueTask DisposeAsync() => nats.DisposeAsync();

    [Fact]
    public async Task Event_GoesFromOneProcessToTheOther()
    {
        using var consumer = await StartConsumerAsync();
        var documentId = Guid.NewGuid();

        var messageId = await PublishAsync(new DocumentCreated(documentId, "Skript Kapitel 1"));

        await consumer.WaitForLineAsync($"handled {messageId}");
        using var context = CreateContext();
        Assert.Equal(documentId, context.Notifications.Single().DocumentId);
        Assert.Equal(messageId, context.Inbox.Single().MessageId);
    }

    // Within its duplicate window (2 minutes by default) JetStream drops the second publish itself.
    [Fact]
    public async Task SameEventPublishedTwice_HasOneEffect()
    {
        using var consumer = await StartConsumerAsync();
        var documentCreated = new DocumentCreated(Guid.NewGuid(), "Skript Kapitel 1");
        var messageId = Guid.NewGuid();

        var first = await PublishAsync(messageId, documentCreated);
        var second = await PublishAsync(messageId, documentCreated);
        var marker = await PublishAsync(new DocumentCreated(Guid.NewGuid(), "Skript Kapitel 2"));

        Assert.False(first.Duplicate);
        Assert.True(second.Duplicate);
        // One consumer gets them in order, so a duplicate would have come before the marker.
        await consumer.WaitForLineAsync($"handled {marker}");
        using var context = CreateContext();
        Assert.Equal(1, context.Notifications.Count(n => n.DocumentId == documentCreated.DocumentId));
        Assert.Equal(2, context.Inbox.Count());
    }

    [Fact]
    public async Task EventRedeliveredAfterACrash_HasOneEffect()
    {
        Guid messageId;
        using (var crashing = await StartConsumerAsync(crashAfterCommit: true))
        {
            messageId = await PublishAsync(new DocumentCreated(Guid.NewGuid(), "Skript Kapitel 1"));
            await crashing.WaitForLineAsync($"handled {messageId}");
            await crashing.WaitForExitAsync();
        }

        using var consumer = await StartConsumerAsync();

        await consumer.WaitForLineAsync($"skipped {messageId}");
        using var context = CreateContext();
        Assert.Equal(1, context.Notifications.Count());
        Assert.Equal(1, context.Inbox.Count());
    }

    [Fact]
    public async Task EventPublishedWhileTheConsumerIsDown_ArrivesWhenItStarts()
    {
        var messageId = await PublishAsync(new DocumentCreated(Guid.NewGuid(), "Skript Kapitel 1"));

        using var consumer = await StartConsumerAsync();

        await consumer.WaitForLineAsync($"handled {messageId}");
        using var context = CreateContext();
        Assert.Equal(1, context.Notifications.Count());
    }

    private async Task<Guid> PublishAsync(DocumentCreated documentCreated)
    {
        var messageId = Guid.NewGuid();
        await PublishAsync(messageId, documentCreated);
        return messageId;
    }

    private async Task<PubAckResponse> PublishAsync(Guid messageId, DocumentCreated documentCreated)
    {
        var ack = await nats.CreateJetStreamContext().PublishAsync(Subject, documentCreated,
            opts: new NatsJSPubOpts { MsgId = messageId.ToString() });

        // A duplicate is already in the stream, so it counts as sent. EnsureSuccess would throw for it.
        if (!ack.Duplicate)
        {
            ack.EnsureSuccess();
        }

        return ack;
    }

    private Task<ConsumerProcess> StartConsumerAsync(bool crashAfterCommit = false) =>
        ConsumerProcess.StartAsync(new Dictionary<string, string>
        {
            ["NATS_URL"] = natsServer.Url,
            ["NATS_STREAM"] = stream,
            ["INBOX_DB"] = connectionString,
            ["CRASH_AFTER_COMMIT"] = crashAfterCommit ? "1" : "0",
        });

    private InboxDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InboxDbContext>()
            .UseMySQL(MySqlConnections.For(connectionString).ConnectionString)
            .Options);
}