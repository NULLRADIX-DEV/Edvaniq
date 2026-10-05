namespace Edvaniq.Services.Planning.Domain;

// A ping as the database stored it. Technical proof that the service writes to and reads from its own database, so it
// belongs to no user and the owner filter leaves it alone.
public sealed class PingRecord(DateTime pingedAt)
{
    public int Id { get; private set; }

    // Always UTC.
    public DateTime PingedAt { get; private set; } = pingedAt;
}
