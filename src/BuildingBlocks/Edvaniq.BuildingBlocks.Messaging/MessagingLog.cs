using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Messaging;

internal static partial class MessagingLog
{
    private static readonly Func<ILogger, string, Guid, IDisposable?> Scope =
        LoggerMessage.DefineScope<string, Guid>("Consumer {Consumer}, message {MessageId}");

    // Names consumer and message on everything logged while handling it, the handler's lines too.
    public static IDisposable? MessageScope(ILogger logger, string consumer, Guid messageId) => Scope(logger, consumer, messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "No broker is configured (ConnectionStrings:nats), messaging stays idle")]
    public static partial void NoBroker(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox dispatch failed, trying again in {Delay}")]
    public static partial void DispatchFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event {MessageId} for {Subject} could not be sent (attempt {Attempts}): {Error}")]
    public static partial void SendFailed(ILogger logger, Guid messageId, string subject, int attempts, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Consumer} stopped, starting it again in {Delay}")]
    public static partial void ConsumerFailed(ILogger logger, Exception exception, string consumer, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Consumer} failed on message {MessageId}, the broker delivers it again")]
    public static partial void HandlerFailed(ILogger logger, Exception exception, string consumer, Guid messageId);

    // Expected and not counted as a failure: the handler waits for something still on its way (MessageNotReadyException).
    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer {Consumer} is not ready for message {MessageId} yet, the broker delivers it again: {Reason}")]
    public static partial void HandlerNotReady(ILogger logger, string consumer, Guid messageId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Consumer} dropped message {MessageId}, it still couldn't be handled after {Delivery} deliveries")]
    public static partial void NotReadyDropped(ILogger logger, string consumer, Guid messageId, int delivery);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer {Consumer} waits for the stream {Stream} of its sender")]
    public static partial void WaitingForStream(ILogger logger, string consumer, string stream);

    // Only after the SaveChanges that stored the effect, so it proves the message took effect.
    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer {Consumer} has handled message {MessageId}")]
    public static partial void Handled(ILogger logger, string consumer, Guid messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consumer {Consumer} has handled message {MessageId} before, skipped")]
    public static partial void AlreadyHandled(ILogger logger, string consumer, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Consumer} got a message without a valid Nats-Msg-Id, dropped")]
    public static partial void MissingMessageId(ILogger logger, string consumer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleanup of old outbox and inbox rows failed")]
    public static partial void CleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleanup deleted {Outbox} sent outbox rows, {Inbox} inbox rows and {DeletedUsers} deleted users")]
    public static partial void CleanedUp(ILogger logger, int outbox, int inbox, int deletedUsers);

    // Without the user id: the user is gone, the log keeps nothing of them.
    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer {Consumer} skipped {Event} of a deleted user")]
    public static partial void DeletedUserSkipped(ILogger logger, string consumer, string @event);
}