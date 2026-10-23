using System.Collections.Concurrent;

using Edvaniq.Testing;

using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// The NATS transport against a real server.
[Collection(NatsCollection.Name)]
public sealed class NatsTransportTests(NatsTestServer nats) : IAsyncLifetime
{
    private static readonly StreamSpec Stream = new("SENDER", "sender.>");

    private readonly NatsClient client = new(nats.Url);
    private NatsTransport transport = null!;

    public async ValueTask InitializeAsync()
    {
        await NatsTransports.DeleteStreamAsync(nats.Url, Stream.Name);
        transport = NatsTransports.Create(nats.Url);
    }

    public async ValueTask DisposeAsync()
    {
        await transport.DisposeAsync();
        await client.DisposeAsync();
    }

    [Fact]
    public async Task EnsureStream_CreatesTheStreamWithItsLimits_AndCanRunAgain()
    {
        await transport.EnsureStreamAsync(Stream, TestContext.Current.CancellationToken);
        await transport.EnsureStreamAsync(Stream, TestContext.Current.CancellationToken);

        var stream = await client.CreateJetStreamContext().GetStreamAsync(Stream.Name, cancellationToken: TestContext.Current.CancellationToken);
        var config = stream.Info.Config;
        Assert.Equal(["sender.>"], config.Subjects);
        Assert.Equal(StreamConfigStorage.File, config.Storage);
        Assert.Equal(TimeSpan.FromDays(7), config.MaxAge);
        Assert.Equal(256L * 1024 * 1024, config.MaxBytes);
    }

    [Fact]
    public async Task Publish_OfAMessageIdTheStreamKnows_CountsAsSent()
    {
        await transport.EnsureStreamAsync(Stream, TestContext.Current.CancellationToken);
        var message = new OutgoingMessage("sender.greeted", Guid.NewGuid(), "{}");

        await transport.PublishAsync(message, TestContext.Current.CancellationToken);
        await transport.PublishAsync(message, TestContext.Current.CancellationToken);

        var stream = await client.CreateJetStreamContext().GetStreamAsync(Stream.Name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1L, stream.Info.State.Messages);
    }

    [Fact]
    public async Task Publish_WithoutAStream_Fails()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            transport.PublishAsync(new OutgoingMessage("sender.greeted", Guid.NewGuid(), "{}"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Consumer_GetsTheTraceParentOfTheMessage()
    {
        const string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        await transport.EnsureStreamAsync(Stream, TestContext.Current.CancellationToken);
        var traced = new OutgoingMessage("sender.greeted", Guid.NewGuid(), "{}", traceParent);
        var untraced = new OutgoingMessage("sender.greeted", Guid.NewGuid(), "{}");
        await transport.PublishAsync(traced, TestContext.Current.CancellationToken);
        await transport.PublishAsync(untraced, TestContext.Current.CancellationToken);

        var received = await ConsumeAsync(2);

        Assert.Equal([traceParent, null], received.Select(message => message.TraceParent));
    }

    // The sender has not created its stream yet. The consumer host waits for it without a warning.
    [Fact]
    public async Task Consumer_OfAStreamThatDoesNotExist_ReportsTheMissingStream()
    {
        var missing = await Assert.ThrowsAsync<StreamMissingException>(() => transport.ConsumeAsync(
            new Subscription(Stream.Name, "receiver-handler", "sender.greeted"),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken));

        Assert.Equal(Stream.Name, missing.Stream);
    }

    [Fact]
    public async Task Consumer_GetsTheMessagesWithTheirMessageId_InOrder()
    {
        await transport.EnsureStreamAsync(Stream, TestContext.Current.CancellationToken);
        var first = new OutgoingMessage("sender.greeted", Guid.NewGuid(), "{\"n\":1}");
        var other = new OutgoingMessage("sender.other", Guid.NewGuid(), "{\"n\":2}");
        var second = new OutgoingMessage("sender.greeted", Guid.NewGuid(), "{\"n\":3}");
        foreach (var message in new[] { first, other, second })
        {
            await transport.PublishAsync(message, TestContext.Current.CancellationToken);
        }

        var received = new ConcurrentQueue<IncomingMessage>();
        using var stop = new CancellationTokenSource();
        var running = transport.ConsumeAsync(
            new Subscription(Stream.Name, "receiver-handler", "sender.greeted"),
            (message, _) =>
            {
                received.Enqueue(message);
                return Task.CompletedTask;
            },
            stop.Token);

        await Wait.UntilAsync(() => received.Count == 2, "The consumer did not get the messages.");
        await stop.CancelAsync();
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal([new IncomingMessage(first.MessageId, first.Payload), new IncomingMessage(second.MessageId, second.Payload)], received);
    }

    // Consumes the subject sender.greeted until count messages have arrived.
    private async Task<IReadOnlyList<IncomingMessage>> ConsumeAsync(int count)
    {
        var received = new ConcurrentQueue<IncomingMessage>();
        using var stop = new CancellationTokenSource();
        var running = transport.ConsumeAsync(
            new Subscription(Stream.Name, "receiver-handler", "sender.greeted"),
            (message, _) =>
            {
                received.Enqueue(message);
                return Task.CompletedTask;
            },
            stop.Token);

        await Wait.UntilAsync(() => received.Count == count, "The consumer did not get the messages.");
        await stop.CancelAsync();
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
        }

        return [.. received];
    }
}