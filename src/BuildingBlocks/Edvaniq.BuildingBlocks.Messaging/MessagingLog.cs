using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Messaging;

internal static partial class MessagingLog
{
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consumer {Consumer} has handled message {MessageId} before, skipped")]
    public static partial void AlreadyHandled(ILogger logger, string consumer, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Consumer} got a message without a valid Nats-Msg-Id, dropped")]
    public static partial void MissingMessageId(ILogger logger, string consumer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleanup of old outbox and inbox rows failed")]
    public static partial void CleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleanup deleted {Outbox} sent outbox rows and {Inbox} inbox rows")]
    public static partial void CleanedUp(ILogger logger, int outbox, int inbox);
}