using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Spikes.Broker.Consumer;

using Microsoft.EntityFrameworkCore;

using NATS.Client.JetStream.Models;
using NATS.Net;

var connectionString = MySqlConnections.For(Required("INBOX_DB")).ConnectionString;
var crashAfterCommit = Environment.GetEnvironmentVariable("CRASH_AFTER_COMMIT") == "1";
var handler = new NotificationHandler(() =>
    new InboxDbContext(new DbContextOptionsBuilder<InboxDbContext>().UseMySQL(connectionString).Options));

await using var nats = new NatsClient(Required("NATS_URL"));
var consumer = await nats.CreateJetStreamContext().CreateOrUpdateConsumerAsync(Required("NATS_STREAM"),
    new ConsumerConfig("notifications")
    {
        AckPolicy = ConsumerConfigAckPolicy.Explicit,
        AckWait = TimeSpan.FromSeconds(2),
    });
Console.WriteLine("ready");

await foreach (var message in consumer.ConsumeAsync<DocumentCreated>())
{
    // The outbox's MessageId, sent as Nats-Msg-Id so JetStream drops duplicate publishes too.
    var messageId = Guid.Parse(message.Headers!["Nats-Msg-Id"]!);
    var handled = handler.Handle(messageId, message.Data!);
    Console.WriteLine($"{(handled ? "handled" : "skipped")} {messageId}");

    if (crashAfterCommit)
    {
        Environment.Exit(1);
    }

    // Ack only after the commit. If we die in between, NATS sends it again and the inbox drops it.
    await message.AckAsync();
}

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is missing.");