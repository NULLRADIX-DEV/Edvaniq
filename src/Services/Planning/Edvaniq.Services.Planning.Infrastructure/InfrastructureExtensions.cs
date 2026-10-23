using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Planning.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:planningdb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "planningdb";

    // The name of the service in lower case. The stream of its events and the subjects below it are named after it.
    public const string ServiceKey = "planning";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<PlanningDbContext>(DatabaseName);
        builder.Services.AddScoped<IExampleItems, ExampleItems>();
        builder.Services.AddScoped<IPings, Pings>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<PlanningDbContext>(ServiceKey);
        builder.Services.AddIntegrationEventHandler<ExampleItemCreated, ExampleItemCreatedHandler>();

        // Removes the data of a user Identity deletes. Stays when the example goes.
        builder.Services.AddUserDataRemover<PlanningDbContext>();
        builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<PlanningDbContext>();
}