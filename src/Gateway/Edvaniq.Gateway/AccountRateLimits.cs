using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Options;

namespace Edvaniq.Gateway;

// Calls that cost AI, answers and session steps are limited per account, the sub of the checked token, so one account
// can't flood a service or the AI bill. A route opts in with RateLimiterPolicy in ReverseProxy, the numbers come from
// RateLimits. The count lives in this process and starts empty after a restart, there is one gateway. Both clients come
// through here, the web host and the app.
public static class AccountRateLimits
{
    public const string Section = "RateLimits";

    public const string Ai = "ai";

    public const string Answers = "answers";

    public const string Sessions = "sessions";

    public static readonly IReadOnlyList<string> Policies = [Ai, Answers, Sessions];

    // The token handler keeps "sub" as it is (AddTokenValidation).
    private const string AccountClaim = "sub";

    // No numbers and no window, the user only needs to know it passes.
    public const string TooMany = "Zu viele Anfragen in kurzer Zeit. Warte einen Moment und versuch es dann noch einmal.";

    // When the limiter names no time: one segment of the window.
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private const int Segments = 6;

    public static IServiceCollection AddAccountRateLimits(this IServiceCollection services)
    {
        services.AddOptions<Dictionary<string, AccountLimit>>()
            .BindConfiguration(Section)
            .Validate(
                limits => Policies.All(policy => limits.TryGetValue(policy, out var limit) && limit.PermitsPerMinute > 0),
                $"{Section} needs PermitsPerMinute above 0 for {string.Join(", ", Policies)}.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            foreach (var policy in Policies)
            {
                options.AddPolicy(policy, context =>
                {
                    // Technical clients have no sub, and none of them calls these routes.
                    if (context.User.FindFirst(AccountClaim)?.Value is not { Length: > 0 } account)
                    {
                        return RateLimitPartition.GetNoLimiter("");
                    }

                    var permits = context.RequestServices.GetRequiredService<IOptions<Dictionary<string, AccountLimit>>>()
                        .Value[policy].PermitsPerMinute;
                    return RateLimitPartition.GetSlidingWindowLimiter(account, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permits,
                        Window = Window,
                        SegmentsPerWindow = Segments,
                        QueueLimit = 0,
                    });
                });
            }

            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var wait = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : Window / Segments;
                rejected.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                rejected.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccountRateLimits))
                    .LogWarning("An account went over the limit for {Path}.", rejected.HttpContext.Request.Path);
                await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: TooMany)
                    .ExecuteAsync(rejected.HttpContext);
            };
        });

        return services;
    }
}

internal sealed class AccountLimit
{
    public int PermitsPerMinute { get; set; }
}