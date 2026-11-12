using Edvaniq.BuildingBlocks.Application;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Infrastructure;

public static partial class DatabaseExtensions
{
    // The connection string is read when the first context is created, not at startup. A service without it still
    // starts and reports not ready (MySqlHealthCheck).
    public static IServiceCollection AddMySqlDbContext<TContext>(this IServiceCollection services, string connectionName)
        where TContext : DbContext =>
        services.AddDbContext<TContext>((provider, options) => options.UseMySQL(MySqlConnections.For(
            provider.GetRequiredService<IConfiguration>().GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' is missing.")).ConnectionString));

    // The remover for TDeletion, the service's whole deletion of a user (usually its UserDeleted handler), and the sweep
    // that runs it again for rows stored while the deletion ran (docs/service-internals.md#nutzer-löschen).
    public static IServiceCollection AddUserDeletion<TContext, TDeletion>(this IServiceCollection services)
        where TContext : ServiceDbContext
        where TDeletion : class, IUserDeletion
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUserDataRemover, UserDataRemover<TContext>>();
        services.AddScoped<IUserDeletion, TDeletion>();
        services.AddScoped<IDeletedUsers, DeletedUsers<TContext>>();
        services.AddHostedService<DeletedUserSweep<TContext>>();
        return services;
    }

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