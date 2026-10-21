namespace Edvaniq.BuildingBlocks.Messaging;

// Stands in while no broker is configured. Dispatcher and consumers see it and stay idle, so a service without a broker
// runs as before.
internal sealed class NoMessageTransport : IMessageTransport
{
    public static NoMessageTransport Instance { get; } = new();

    public Task EnsureStreamAsync(StreamSpec stream, CancellationToken cancellationToken) => throw NoBroker();

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) => throw NoBroker();

    public Task ConsumeAsync(
        Subscription subscription,
        Func<IncomingMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken) => throw NoBroker();

    private static InvalidOperationException NoBroker() => new("No broker is configured.");
}

internal static class MessageTransportExtensions
{
    public static bool IsConfigured(this IMessageTransport transport) => transport is not NoMessageTransport;
}