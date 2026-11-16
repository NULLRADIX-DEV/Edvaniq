using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Edvaniq.Testing;

public static class TestResilience
{
    // The standard resilience of every HttpClient, made for tests: retries of a stub that is down come at once, and the
    // circuit breaker never opens. One factory serves a whole test class, so a breaker opened by one test's outage would
    // cut off the next test with BrokenCircuitException.
    public static IServiceCollection AddTestResilience(this IServiceCollection services) =>
        services.ConfigureAll<HttpStandardResilienceOptions>(options =>
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.CircuitBreaker.MinimumThroughput = int.MaxValue;
        });
}