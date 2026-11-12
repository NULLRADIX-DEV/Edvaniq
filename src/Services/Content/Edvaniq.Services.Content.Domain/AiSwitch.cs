namespace Edvaniq.Services.Content.Domain;

// The operator's emergency stop for every AI call of Content. One row at most, none means running. Belongs to nobody:
// no user and no document, only whether the AI is paused and since when.
public sealed class AiSwitch
{
    public const int TheOne = 1;

    private AiSwitch(DateTime changedAt) => ChangedAt = changedAt;

    public int Id { get; private set; } = TheOne;

    public bool Paused { get; private set; }

    // UTC.
    public DateTime ChangedAt { get; private set; }

    public static AiSwitch Running(DateTime now) => new(now);

    // Whether this changed anything.
    public bool Set(bool paused, DateTime now)
    {
        if (Paused == paused)
        {
            return false;
        }

        Paused = paused;
        ChangedAt = now;
        return true;
    }
}