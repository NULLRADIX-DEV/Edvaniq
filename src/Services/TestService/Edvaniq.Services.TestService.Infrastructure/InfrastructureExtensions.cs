using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.TestService.Application;
using Edvaniq.Services.TestService.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.TestService.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:testservicedb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "testservicedb";

    // The name of the service in lower case. The stream of its events and the subjects below it are named after it.
    public const string ServiceKey = "testservice";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<TestServiceDbContext>(DatabaseName);
        builder.Services.AddScoped<IBookmarks, Bookmarks>();

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<TestServiceDbContext>(ServiceKey);
        builder.Services.AddIntegrationEventHandler<BookmarkAdded, BookmarkAddedHandler>();

        // Stays when the example goes.
        builder.Services.AddUserDataRemover<TestServiceDbContext>();
        builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<TestServiceDbContext>();
}