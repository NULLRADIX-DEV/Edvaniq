namespace Edvaniq.BuildingBlocks.Messaging;

// Proof that a consumer has handled a message. It is stored in the same SaveChanges as the effect of the handler.
public sealed class InboxMessage
{
    public const int ConsumerMaxLength = 128;

    public Guid MessageId { get; set; }

    // The durable consumer, one per handler: two handlers of one event each handle it once.
    public required string Consumer { get; set; }

    public DateTime ReceivedAt { get; set; }
}