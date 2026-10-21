using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.ServiceName.Application;
using Edvaniq.Services.ServiceName.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.ServiceName.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:servicenamedb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "servicenamedb";

    // The name of the service in lower case. The stream of its events and the subjects below it are named after it.
    public const string ServiceKey = "servicename";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<ServiceNameDbContext>(DatabaseName);
        builder.Services.AddScoped<IExampleItems, ExampleItems>();

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<ServiceNameDbContext>(ServiceKey);
        builder.Services.AddIntegrationEventHandler<ExampleItemCreated, ExampleItemCreatedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<ServiceNameDbContext>();
}