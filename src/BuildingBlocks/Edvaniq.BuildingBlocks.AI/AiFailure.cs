using System.Net;

namespace Edvaniq.BuildingBlocks.AI;

// Tells a failure of the provider that may pass from one that stays until someone changes something.
public static class AiFailure
{
    // Transient: the attempt timed out, the network failed, or the provider said it's busy or broken right now (408,
    // 429, 5xx). Anything else, like a wrong key (401) or a request it refuses (400), fails the same way again.
    public static bool IsTransient(Exception failure) => failure switch
    {
        OperationCanceledException or TimeoutException or IOException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } =>
            status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500,
        _ => false,
    };

    // How long the provider asked to wait, or null if it didn't say. A date in the past counts as no wait.
    public static TimeSpan? RetryAfter(Exception failure, DateTime now) => failure switch
    {
        AiProviderException { RetryAfter.Delta: { } delta } => delta,
        AiProviderException { RetryAfter.Date: { } date } => date.UtcDateTime > now ? date.UtcDateTime - now : TimeSpan.Zero,
        _ => null,
    };
}