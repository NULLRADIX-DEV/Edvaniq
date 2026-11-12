using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Knowledge.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Knowledge.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:knowledgedb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "knowledgedb";

    // The name of the service in lower case. The stream of its events and the subjects below it are named after it.
    public const string ServiceKey = "knowledge";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<KnowledgeDbContext>(DatabaseName);
        builder.Services.AddScoped<ITopics, Topics>();
        builder.Services.AddScoped<IAnalyzedTopics, AnalyzedTopics>();
        builder.Services.AddScoped<ITopicState, TopicState>();
        builder.Services.AddScoped<IDeletedDocuments, DeletedDocuments>();
        builder.Services.AddScoped<TopicsChangedEvents>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<KnowledgeDbContext>(ServiceKey);

        builder.Services.AddUserDeletion<KnowledgeDbContext, UserDeletedHandler>();
        builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();
        builder.Services.AddIntegrationEventHandler<DocumentAnalyzed, DocumentAnalyzedHandler>();
        builder.Services.AddIntegrationEventHandler<DocumentDeleted, DocumentDeletedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<KnowledgeDbContext>();
}