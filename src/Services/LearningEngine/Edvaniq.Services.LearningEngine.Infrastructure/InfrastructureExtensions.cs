using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.Planning.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.LearningEngine.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:learningenginedb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "learningenginedb";

    // The name of the service in lower case. The stream of its events and the subjects below it are named after it.
    public const string ServiceKey = "learningengine";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<LearningEngineDbContext>(DatabaseName);
        builder.Services.AddScoped<ILearningRecords, LearningRecords>();
        builder.Services.AddScoped<IKnownTopics, KnownTopics>();
        builder.Services.AddScoped<IMasteries, Masteries>();
        builder.Services.AddScoped<ITopicIntroductions, TopicIntroductions>();
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<LearningEngineDbContext>(ServiceKey);
        builder.Services.AddIntegrationEventHandler<AnswerRecorded, AnswerRecordedHandler>();
        builder.Services.AddIntegrationEventHandler<TopicsChanged, TopicsChangedHandler>();
        builder.Services.AddIntegrationEventHandler<SessionCompleted, SessionCompletedHandler>();

        // Every IOwnedByUser entity, learning records included.
        builder.Services.AddUserDataRemover<LearningEngineDbContext>();
        builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<LearningEngineDbContext>();
}