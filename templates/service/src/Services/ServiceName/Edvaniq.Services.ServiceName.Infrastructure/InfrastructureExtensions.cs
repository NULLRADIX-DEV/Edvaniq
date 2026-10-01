using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.ServiceName.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.ServiceName.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:servicenamedb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "servicenamedb";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<ServiceNameDbContext>(DatabaseName);
        builder.Services.AddScoped<IExampleItems, ExampleItems>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<ServiceNameDbContext>();
}
