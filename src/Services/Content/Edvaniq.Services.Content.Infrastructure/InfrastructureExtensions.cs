using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Infrastructure.Files;
using Edvaniq.Services.Identity.Contracts;

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
        builder.Services.AddFileStore();
        builder.Services.AddScoped<StoredFiles>();
        builder.Services.AddScoped<IFiles>(services => services.GetRequiredService<StoredFiles>());
        builder.Services.AddScoped<IUserFiles>(services => services.GetRequiredService<StoredFiles>());
        builder.Services.AddHostedService<FileSweeper>();
        builder.Services.AddScoped<AiUsageReport>();
        builder.Services.AddSingleton<AiUsageLog>();

        // Outbox, dispatcher and consumers. They only run when a broker is configured (ConnectionStrings:nats).
        builder.Services.AddMessaging<ContentDbContext>(ServiceKey);

        builder.Services.AddUserDataRemover<ContentDbContext>();
        builder.Services.AddScoped<IAiUsageArchive, AiUsageArchive>();
        builder.Services.AddIntegrationEventHandler<UserDeleted, UserDeletedHandler>();

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // The worker: the same database, without a user, and the jobs that run in the background. It sends events through
    // the outbox but handles none, the API consumes them. A second consumer here would handle every event twice. Both
    // dispatchers send from the same outbox, each takes other rows. QuestionPools needs an IKnowledgeTopics, the worker
    // registers it with its own token.
    public static TBuilder AddWorkerInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<ContentDbContext>(DatabaseName);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ICurrentUser, NoUser>();
        builder.Services.AddFileStore();
        builder.Services.AddMessaging<ContentDbContext>(ServiceKey);
        builder.Services.AddSingleton<AiUsageLog>();
        builder.Services.AddHostedService<FileTextExtractor>();
        builder.Services.AddHostedService<DocumentSplitter>();
        builder.Services.AddHostedService<DocumentAnalyzer>();
        builder.Services.AddHostedService<QuestionPools>();

        return builder;
    }

    // Only for the operator's document delete command, never in the running API.
    public static IServiceCollection AddDocumentOwners(this IServiceCollection services) =>
        services.AddScoped<IDocumentOwners, DocumentOwners>();

    // Without a directory neither process starts. The API writes to it, the worker only reads (FileStorage:Directory).
    private static void AddFileStore(this IServiceCollection services)
    {
        services.AddOptions<FileStorageOptions>()
            .BindConfiguration(FileStorageOptions.Section)
            .Validate(options => options.Directory.Length > 0, "FileStorage:Directory is missing.")
            .ValidateOnStart();
        services.AddSingleton<FileStore>();
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<ContentDbContext>();
}