using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Knowledge.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Assessment.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:assessmentdb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "assessmentdb";

    // The name of the service in lower case. The stream of its events and the subjects below it are named after it.
    public const string ServiceKey = "assessment";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<AssessmentDbContext>(DatabaseName);
        builder.Services.AddScoped<IQuestions, Questions>();
        builder.Services.AddScoped<IAttempts, Attempts>();
        builder.Services.AddScoped<Quiz>();

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<AssessmentDbContext>(ServiceKey);
        builder.Services.AddIntegrationEventHandler<ItemsGenerated, ItemsGeneratedHandler>();
        builder.Services.AddScoped<IKnownTopics, KnownTopics>();
        builder.Services.AddIntegrationEventHandler<TopicsChanged, TopicsChangedHandler>();

        // Every IOwnedByUser entity, questions included.
        builder.Services.AddUserDataRemover<AssessmentDbContext>();
        builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<AssessmentDbContext>();
}