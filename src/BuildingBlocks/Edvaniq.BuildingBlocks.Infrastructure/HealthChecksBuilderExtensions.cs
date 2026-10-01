using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Edvaniq.BuildingBlocks.Infrastructure;

public static class HealthChecksBuilderExtensions
{
    // Checks without the "live" tag only count for ready (/health in ServiceDefaults), never for alive (/alive). A
    // service whose database is down keeps running, it just takes no traffic.
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddMySqlReadinessCheck(this IHealthChecksBuilder builder, string connectionName) =>
        builder.Add(new HealthCheckRegistration(
            connectionName,
            services => new MySqlHealthCheck(services.GetRequiredService<IConfiguration>(), connectionName),
            failureStatus: null,
            tags: [ReadyTag],
            timeout: MySqlHealthCheck.Timeout));
}
