using System.Threading.RateLimiting;

using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Api.Tokens;

// At most AttemptsPerMinute logins per account name, right or wrong, so nobody tries a list of passwords on one account.
// Counted by the normalized name, "Ben" and "ben" share their attempts. Only in memory, a restart starts again. The
// limit per caller is the web host's, Identity only sees the gateway.
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

    // False if the name has used up its attempts. Names that are not valid share one partition.
    public bool TryAttempt(string? name, out TimeSpan retryAfter)
    {
        using var lease = limiter.AttemptAcquire(AccountName.Normalize(name) ?? "");
        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}