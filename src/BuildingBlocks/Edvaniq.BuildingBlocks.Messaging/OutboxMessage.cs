namespace Edvaniq.BuildingBlocks.Messaging;

// An integration event waiting to go to the broker. It is stored in the same SaveChanges as the change it belongs to.
public sealed class OutboxMessage
{
    public const int SubjectMaxLength = 200;

    public const int LastErrorMaxLength = 2000;

    // Insert order, the dispatcher sends by it.
    public long Id { get; set; }

    // Goes out as Nats-Msg-Id and is what an inbox knows the event by. We only guarantee at least once.
    public Guid MessageId { get; set; }

    public required string Subject { get; set; }

    public required string Payload { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime? SentAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}