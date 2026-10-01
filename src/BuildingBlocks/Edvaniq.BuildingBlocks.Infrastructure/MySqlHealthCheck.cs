using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySql.Data.MySqlClient;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Ready only if the service reaches its own database. A missing connection string counts as not ready, so a service
// without its database fails loudly instead of reporting healthy.
internal sealed class MySqlHealthCheck(IConfiguration configuration, string connectionName) : IHealthCheck
{
    // Per step (connect, query). Both together stay below the timeout of the Docker health check (deploy/compose.yml).
    private const int StepTimeoutSeconds = 2;

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2 * StepTimeoutSeconds);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Connection string '{connectionName}' is missing.");
        }

        try
        {
            // Synchronous on purpose: when the server accepts the connection but never answers (a frozen container),
            // MySql.Data's OpenAsync ignores both the connect timeout and the token and hangs for good. Open() gives up
            // after the timeout and closes the socket. It holds one pool thread for a few seconds at most.
            await Task.Run(() => Ping(connectionString), CancellationToken.None).WaitAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: exception);
        }
    }

    private static void Ping(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            ConnectionTimeout = StepTimeoutSeconds,
            DefaultCommandTimeout = StepTimeoutSeconds,
        };

        using var connection = new MySqlConnection(builder.ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.ExecuteScalar();
    }
}
