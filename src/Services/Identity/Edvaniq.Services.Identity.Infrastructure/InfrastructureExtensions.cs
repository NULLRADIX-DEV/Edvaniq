using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Identity.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Identity.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:identitydb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "identitydb";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<IdentityDbContext>(DatabaseName);
        builder.Services.TryAddScoped<ICurrentUser, NoCurrentUser>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IAccounts, Accounts>();
        builder.Services.AddScoped<AccountService>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<IdentityDbContext>();
}