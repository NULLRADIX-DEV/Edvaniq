using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NATS.Client.JetStream;
using NATS.Net;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

internal static class Wait
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // Waits for something that happens in the background of a service. The logs of the services go into the failure.
    public static async Task UntilAsync(Func<bool> condition, string because, params Func<string>[] logs)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                $"{because}{Environment.NewLine}{string.Join(Environment.NewLine, logs.Select(log => log()))}");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}

// Passes everything to the real transport, but the first message a consumer has handled looks as if its confirmation got
// lost: the handler has returned and its changes are stored, then the call fails. That is what a crash between commit and
// confirmation looks like, and the broker delivers the message again.
internal sealed class LosingAcks(IMessageTransport inner) : IMessageTransport, IAsyncDisposable
{
    private int lost;
    private int deliveries;

    // How often the handler of the consumer was called, also for the messages the inbox knew.
    public int Deliveries => Volatile.Read(ref deliveries);

    public Task EnsureStreamAsync(StreamSpec stream, CancellationToken cancellationToken) =>
        inner.EnsureStreamAsync(stream, cancellationToken);

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) =>
        inner.PublishAsync(message, cancellationToken);

    public Task ConsumeAsync(
        Subscription subscription,
        Func<IncomingMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken) =>
        inner.ConsumeAsync(
            subscription,
            async (message, token) =>
            {
                Interlocked.Increment(ref deliveries);
                await onMessage(message, token);
                if (Interlocked.Exchange(ref lost, 1) == 0)
                {
                    throw new InvalidOperationException("The confirmation got lost.");
                }
            },
            cancellationToken);

    public ValueTask DisposeAsync() => inner is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;
}

internal static class NatsTransports
{
    // A transport to the test server, with the settings of the tests.
    public static NatsTransport Create(string url, string service = "sender") =>
        new(url, Options.Create(new MessagingOptions { Service = service, AckWait = TimeSpan.FromSeconds(2) }), NullLogger<NatsTransport>.Instance);

    // A stream outlives the test that made it and the server is shared, so a test clears its own first.
    public static async Task DeleteStreamAsync(string url, string stream)
    {
        await using var client = new NatsClient(url);
        try
        {
            await client.CreateJetStreamContext().DeleteStreamAsync(stream, TestContext.Current.CancellationToken);
        }
        catch (NatsJSApiException)
        {
            // There was none.
        }
    }
}