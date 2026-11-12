using System.Threading.RateLimiting;

using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Api.Tokens;

// At most AttemptsPerMinute logins per account, right or wrong, so nobody tries a list of passwords on one account.
// Counted by the normalized login, "Ben" and "ben" share their attempts, an address only by its fingerprint, so no
// address sits in memory. Only in memory, a restart starts again. The limit per caller is the web host's, Identity only
// sees the gateway.
internal sealed class LoginThrottle : IDisposable
{
    public const int AttemptsPerMinute = 5;

    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(
        name => RateLimitPartition.GetFixedWindowLimiter(name, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = AttemptsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // False if the login has used up its attempts. Logins that are not valid share one partition.
    public bool TryAttempt(string? login, out TimeSpan retryAfter)
    {
        using var lease = limiter.AttemptAcquire(KeyOf(login));
        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
        return lease.IsAcquired;
    }

    // A name can't contain "@" or ":", so the two kinds never share a key.
    internal static string KeyOf(string? login) => AccountEmail.IsEmail(login)
        ? AccountEmail.Normalize(login) is { } email ? "email:" + AccountEmail.Fingerprint(email) : ""
        : AccountName.Normalize(login) ?? "";

    public void Dispose() => limiter.Dispose();
}