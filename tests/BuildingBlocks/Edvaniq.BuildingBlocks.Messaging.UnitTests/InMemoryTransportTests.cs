using System.Collections.Concurrent;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// The in-memory broker behaves like JetStream where the services rely on it.
public class InMemoryTransportTests
{
    private static readonly Subscription Pings = new("ALPHA", "beta-handler", "alpha.pinged");

    [Fact]
    public async Task Consumer_GetsTheMessagesOfItsSubject_InOrder()
    {
        var transport = new InMemoryTransport();
        await using var consumer = new TestConsumer(transport, Pings);
        var first = Message("alpha.pinged");
        var other = Message("alpha.other");
        var second = Message("alpha.pinged");

        foreach (var message in new[] { first, other, second })
        {
            await transport.PublishAsync(message, TestContext.Current.CancellationToken);
        }

        await Wait.UntilAsync(() => consumer.Received.Count == 2, "The consumer did not get both messages.");
        Assert.Equal([first.MessageId, second.MessageId], consumer.Received.Select(message => message.MessageId));
    }

    [Fact]
    public async Task MessageWithTheSameId_IsDropped()
    {
        var transport = new InMemoryTransport();
        var message = Message("alpha.pinged");

        await transport.PublishAsync(message, TestContext.Current.CancellationToken);
        await transport.PublishAsync(message, TestContext.Current.CancellationToken);

        Assert.Single(transport.Published);
    }

    [Fact]
    public async Task MessageWithTheSameId_ArrivesTwice_WhenDuplicatesAreNotDropped()
    {
        var transport = new InMemoryTransport { DropsDuplicates = false };
        await using var consumer = new TestConsumer(transport, Pings);
        var message = Message("alpha.pinged");

        await transport.PublishAsync(message, TestContext.Current.CancellationToken);
        await transport.PublishAsync(message, TestContext.Current.CancellationToken);

        await Wait.UntilAsync(() => consumer.Received.Count == 2, "The duplicate did not arrive.");
    }

    [Fact]
    public async Task DurableConsumer_ContinuesWhereItStopped()
    {
        var transport = new InMemoryTransport();
        var first = Message("alpha.pinged");
        var second = Message("alpha.pinged");

        await transport.PublishAsync(first, TestContext.Current.CancellationToken);
        await using (var before = new TestConsumer(transport, Pings))
        {
            await Wait.UntilAsync(() => before.Received.Count == 1, "The first message did not arrive.");
        }

        await transport.PublishAsync(second, TestContext.Current.CancellationToken);
        await using var after = new TestConsumer(transport, Pings);

        await Wait.UntilAsync(() => after.Received.Count == 1, "The second message did not arrive.");
        Assert.Equal(second.MessageId, after.Received.Single().MessageId);
    }

    [Fact]
    public async Task OtherConsumer_GetsEverythingFromTheStart()
    {
        var transport = new InMemoryTransport();
        await transport.PublishAsync(Message("alpha.pinged"), TestContext.Current.CancellationToken);
        await using var consumer = new TestConsumer(transport, Pings with { Consumer = "gamma-handler" });

        await Wait.UntilAsync(() => consumer.Received.Count == 1, "A new consumer did not get the old message.");
    }

    [Fact]
    public async Task MessageWhoseHandlerThrew_IsDeliveredAgain()
    {
        var transport = new InMemoryTransport { RedeliveryDelay = TimeSpan.FromMilliseconds(10) };
        var attempts = 0;
        await using var consumer = new TestConsumer(transport, Pings, _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("Not now.");
            }

            return Task.CompletedTask;
        });
        var message = Message("alpha.pinged");

        await transport.PublishAsync(message, TestContext.Current.CancellationToken);

        await Wait.UntilAsync(() => consumer.Received.Count == 1, "The message was not delivered again.");
        Assert.Equal(2, attempts);
        Assert.Equal(message.MessageId, consumer.Received.Single().MessageId);
    }

    [Fact]
    public async Task FailingBroker_RefusesStreamsAndMessages_UntilItWorksAgain()
    {
        var transport = new InMemoryTransport { Failing = true };
        var message = Message("alpha.pinged");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.EnsureStreamAsync(new StreamSpec("ALPHA", "alpha.>"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.PublishAsync(message, TestContext.Current.CancellationToken));
        Assert.Empty(transport.Published);

        transport.Failing = false;
        await transport.EnsureStreamAsync(new StreamSpec("ALPHA", "alpha.>"), TestContext.Current.CancellationToken);
        await transport.PublishAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(["ALPHA"], transport.Streams);
        Assert.Single(transport.Published);
    }

    [Fact]
    public async Task Consumer_GetsTheTraceParentOfTheMessage()
    {
        const string traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
        var transport = new InMemoryTransport();
        await using var consumer = new TestConsumer(transport, Pings);

        await transport.PublishAsync(Message("alpha.pinged") with { TraceParent = traceParent }, TestContext.Current.CancellationToken);

        await Wait.UntilAsync(() => consumer.Received.Count == 1, "The consumer did not get the message.");
        Assert.Equal(traceParent, Assert.Single(consumer.Received).TraceParent);
    }

    private static OutgoingMessage Message(string subject) => new(subject, Guid.NewGuid(), "{}");

    // A consumer that runs in the background until the test ends.
    private sealed class TestConsumer : IAsyncDisposable
    {
        private readonly CancellationTokenSource stop = new();
        private readonly Task running;

        public TestConsumer(InMemoryTransport transport, Subscription subscription, Func<IncomingMessage, Task>? onMessage = null) =>
            running = transport.ConsumeAsync(
                subscription,
                async (message, _) =>
                {
                    if (onMessage is not null)
                    {
                        await onMessage(message);
                    }

                    Received.Enqueue(message);
                },
                stop.Token);

        public ConcurrentQueue<IncomingMessage> Received { get; } = new();

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }

            stop.Dispose();
        }
    }
}