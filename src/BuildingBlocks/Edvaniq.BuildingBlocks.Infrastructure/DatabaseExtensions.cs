using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Infrastructure;

public static partial class DatabaseExtensions
{
    // The connection string is read when the first context is created, not at startup. A service without it still
    // starts and reports not ready (MySqlHealthCheck).
    public static IServiceCollection AddMySqlDbContext<TContext>(this IServiceCollection services, string connectionName)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((provider, options) => options.UseMySQL(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' is missing.")));

    // Applies all pending migrations and returns the exit code. A second run finds nothing to do. Like Run(), it
    // disposes the host at the end, so the last log lines get out.
    public static int RunMigrations<TContext>(this IHost host) where TContext : DbContext
    {
        using (host)
        {
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseExtensions));
            try
            {
                using var scope = host.Services.CreateScope();

                // Synchronous on purpose: MySql.Data's OpenAsync can hang for good (see MySqlHealthCheck).
                scope.ServiceProvider.GetRequiredService<TContext>().Database.Migrate();

                LogUpToDate(logger, typeof(TContext).Name);
                return 0;
            }
            catch (Exception exception)
            {
                LogFailed(logger, exception, typeof(TContext).Name);
                return 1;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database of {Context} is up to date")]
    private static partial void LogUpToDate(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migration of {Context} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string context);
}
