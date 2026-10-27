using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:contentdb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "contentdb";

    // The name of the service in lower case. The stream of its events and the subjects below it are named after it.
    public const string ServiceKey = "content";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<ContentDbContext>(DatabaseName);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IDocuments, Documents>();

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<ContentDbContext>(ServiceKey);

        builder.Services.AddUserDataRemover<ContentDbContext>();
        builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // The worker: the same database, without a user, and the jobs that run in the background. It sends events through
    // the outbox but handles none, the API consumes them. A second consumer here would handle every event twice. Both
    // dispatchers send from the same outbox, each takes other rows.
    public static TBuilder AddWorkerInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<ContentDbContext>(DatabaseName);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ICurrentUser, NoUser>();
        builder.Services.AddMessaging<ContentDbContext>(ServiceKey);
        builder.Services.AddHostedService<DocumentSplitter>();
        builder.Services.AddHostedService<DocumentAnalyzer>();

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<ContentDbContext>();
}