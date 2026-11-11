using System.Net;
using System.Net.Http.Headers;

namespace Edvaniq.BuildingBlocks.AI;

// The provider answered with an error status. Carries the status and its Retry-After, never a header with the key or
// the body, which may echo the prompt.
public sealed class AiProviderException(HttpStatusCode status, RetryConditionHeaderValue? retryAfter)
    : HttpRequestException($"The AI provider answered {(int)status}.", null, status)
{
    public RetryConditionHeaderValue? RetryAfter { get; } = retryAfter;
}