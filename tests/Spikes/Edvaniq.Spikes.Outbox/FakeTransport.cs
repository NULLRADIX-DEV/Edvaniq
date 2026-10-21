using System.Collections.Concurrent;

namespace Edvaniq.Spikes.Outbox;

public sealed class FakeTransport : ITransport
{
    private readonly ConcurrentDictionary<Guid, int> received = new();

    public bool Failing { get; set; }

    public IReadOnlyDictionary<Guid, int> Received => received;

    public void Send(OutboxMessage message)
    {
        if (Failing)
        {
            throw new TransportException("Broker unreachable");
        }

        received.AddOrUpdate(message.MessageId, 1, (_, count) => count + 1);
    }
}