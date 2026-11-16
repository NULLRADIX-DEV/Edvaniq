using System.Collections.Concurrent;

using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Api.Tokens;

// The temporary lock after failed logins (#252, docs/tokens.md): MaxFailures wrong logins within Window lock the login
// for LockedFor, even with the right password, and a login that went through starts counting anew. It counts by the
// normalized login, an address only by its fingerprint, and the same way for a login no account has: whether an account
// exists makes no difference. Kept in memory only, so nothing about failed logins lies in the database; a restart lifts
// every lock. The limit per caller is the web host's, Identity only sees the gateway.
internal sealed class LoginLock(TimeProvider time)
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan LockedFor = TimeSpan.FromMinutes(15);

    // Beyond this many logins in memory the ones that count nothing anymore are dropped.
    private const int Tidy = 10_000;

    private readonly ConcurrentDictionary<string, Entry> logins = new(StringComparer.Ordinal);

    // True and the wait while the login is locked.
    public bool IsLocked(string? login, out TimeSpan retryAfter)
    {
        var now = time.GetUtcNow();
        retryAfter = TimeSpan.Zero;
        if (!logins.TryGetValue(KeyOf(login), out var entry))
        {
            return false;
        }

        lock (entry)
        {
            if (entry.LockedUntil is { } until && until > now)
            {
                retryAfter = until - now;
                return true;
            }

            return false;
        }
    }

    // A wrong password or an unknown login. The last one of MaxFailures within Window locks it.
    public void Failed(string? login)
    {
        var now = time.GetUtcNow();
        if (logins.Count > Tidy)
        {
            foreach (var (key, stale) in logins.Where(item => item.Value.IsStale(now)))
            {
                logins.TryRemove(new KeyValuePair<string, Entry>(key, stale));
            }
        }

        var entry = logins.GetOrAdd(KeyOf(login), _ => new Entry());
        lock (entry)
        {
            if (entry.LockedUntil is { } until && until <= now || now - entry.FirstFailure >= Window)
            {
                entry.Failures = 0;
                entry.LockedUntil = null;
            }

            if (entry.Failures == 0)
            {
                entry.FirstFailure = now;
            }

            if (++entry.Failures >= MaxFailures)
            {
                entry.LockedUntil = now + LockedFor;
            }
        }
    }

    // The login went through, or its password was set anew: it counts from zero.
    public void Clear(string? login) => logins.TryRemove(KeyOf(login), out _);

    // A name can't contain "@" or ":", so the two kinds never share a key. Logins that are no valid ones share one.
    internal static string KeyOf(string? login) => AccountEmail.IsEmail(login)
        ? AccountEmail.Normalize(login) is { } email ? "email:" + AccountEmail.Fingerprint(email) : ""
        : AccountName.Normalize(login) ?? "";

    private sealed class Entry
    {
        public int Failures { get; set; }

        public DateTimeOffset FirstFailure { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }

        public bool IsStale(DateTimeOffset now) =>
            LockedUntil is { } until ? until <= now : now - FirstFailure >= Window;
    }
}