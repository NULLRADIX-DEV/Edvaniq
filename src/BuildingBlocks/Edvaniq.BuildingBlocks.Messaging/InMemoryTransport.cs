namespace Edvaniq.BuildingBlocks.Messaging;

// A broker inside the process, for tests. Like JetStream it keeps every message, drops a second message with the same
// MessageId and lets a durable consumer pick up where it left off. It does not expire messages.
public sealed class InMemoryTransport : IMessageTransport
{
    private readonly object gate = new();
    private readonly List<OutgoingMessage> messages = [];
    private readonly HashSet<Guid> knownIds = [];
    private readonly HashSet<string> streams = [];
    private readonly Dictionary<(string Stream, string Consumer), int> positions = [];
    private TaskCompletionSource arrived = NewSignal();
    private volatile bool failing;
    private volatile bool dropsDuplicates = true;

    // While true, creating a stream and sending throw, like a broker nobody can reach. Consumers keep their messages.
    public bool Failing
    {
        get => failing;
        set => failing = value;
    }

    // Like JetStream inside its duplicate window. A test that wants the same message to arrive twice, as it can long after
    // the window, switches it off.
    public bool DropsDuplicates
    {
        get => dropsDuplicates;
        set => dropsDuplicates = value;
    }

    // How long a consumer waits before it gets a message again whose handler threw.
    public TimeSpan RedeliveryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    public IReadOnlyList<OutgoingMessage> Published
    {
        get
        {
            lock (gate)
            {
                return [.. messages];
            }
        }
    }

    public IReadOnlyCollection<string> Streams
    {
        get
        {
            lock (gate)
            {
                return [.. streams];
            }
        }
    }

    public Task EnsureStreamAsync(StreamSpec stream, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        lock (gate)
        {
            streams.Add(stream.Name);
        }

        return Task.CompletedTask;
    }

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        ThrowIfFailing();
        TaskCompletionSource previous;
        lock (gate)
        {
            if (!knownIds.Add(message.MessageId) && dropsDuplicates)
            {
                return Task.CompletedTask;
            }

            messages.Add(message);
            previous = arrived;
            arrived = NewSignal();
        }

        previous.SetResult();
        return Task.CompletedTask;
    }

    public async Task ConsumeAsync(
        Subscription subscription,
        Func<IncomingMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var consumer = (subscription.Stream, subscription.Consumer);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OutgoingMessage? next = null;
            var index = 0;
            Task changed;
            lock (gate)
            {
                index = positions.GetValueOrDefault(consumer);
                while (index < messages.Count && messages[index].Subject != subscription.Subject)
                {
                    index++;
                }

                if (index < messages.Count)
                {
                    next = messages[index];
                }

                changed = arrived.Task;
            }

            if (next is null)
            {
                await changed.WaitAsync(cancellationToken);
                continue;
            }

            try
            {
                await onMessage(new IncomingMessage(next.MessageId, next.Payload), cancellationToken);
                lock (gate)
                {
                    positions[consumer] = index + 1;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RedeliveryDelay, cancellationToken);
            }
        }
    }

    private void ThrowIfFailing()
    {
        if (failing)
        {
            throw new InvalidOperationException("The in-memory broker is switched to failing.");
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}