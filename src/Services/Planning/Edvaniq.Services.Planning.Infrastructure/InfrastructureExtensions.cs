using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Planning.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:planningdb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "planningdb";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<PlanningDbContext>(DatabaseName);
        builder.Services.AddScoped<IExampleItems, ExampleItems>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<PlanningDbContext>();
}
