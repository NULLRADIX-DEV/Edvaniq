namespace Edvaniq.Services.Planning.Domain;

// Active learning time (docs/spec/glossary.md#aktive-lernzeit, #394): only inside a session and only between two signals
// of the user no further apart than IdleLimit. A longer gap counts nothing at all, not even up to the limit: who goes on
// after half an hour wasn't active for five minutes of it. Both numbers are a decision of the team, a constant and not
// configuration, so the time counts the same everywhere.
public static class ActiveTime
{
    // An answer takes under a minute, a part of the learning unit two or three to read. Five minutes covers that, a break
    // doesn't fit in.
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(5);

    // The time between the last signal and this one that counts. Never negative, a signal that arrives out of order or at
    // the same moment counts nothing.
    public static TimeSpan Between(DateTime last, DateTime now)
    {
        var gap = now - last;
        return gap <= TimeSpan.Zero || gap > IdleLimit ? TimeSpan.Zero : gap;
    }
}