namespace Edvaniq.BuildingBlocks.Messaging;

// The way to the broker. The outbox dispatcher and the consumers only know this, so a test swaps the broker out
// (InMemoryTransport) and a service never sees NATS.
public interface IMessageTransport
{
    // Creates the stream of a service or brings it to the current settings. Safe to call at every start.
    Task EnsureStreamAsync(StreamSpec stream, CancellationToken cancellationToken);

    // Returns once the broker has the message. A message it already has under the same MessageId counts as sent, and
    // anything else that goes wrong throws.
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);

    // Hands the messages of a durable consumer to onMessage, one after the other, until cancellationToken ends. The
    // transport confirms a message to the broker when onMessage returns and has it delivered again when onMessage throws.
    // A broker that knows streams, like NATS, throws StreamMissingException while the stream of the sender does not exist.
    Task ConsumeAsync(
        Subscription subscription,
        Func<IncomingMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken);
}

// The stream of a service. Subjects is the filter for every subject below it, such as planning.>.
public sealed record StreamSpec(string Name, string Subjects);

// TraceParent is the W3C traceparent of the request that stored the event (MessageTrace), null without one.
public sealed record OutgoingMessage(string Subject, Guid MessageId, string Payload, string? TraceParent = null);

public sealed record IncomingMessage(Guid MessageId, string Payload, string? TraceParent = null);

// A durable consumer of one subject in a stream. It keeps its place when the process stops.
public sealed record Subscription(string Stream, string Consumer, string Subject);