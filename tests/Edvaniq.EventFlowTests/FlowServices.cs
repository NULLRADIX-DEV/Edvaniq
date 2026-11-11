extern alias assessment;
extern alias knowledge;
extern alias learningengine;
extern alias planning;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using AssessmentInfra = Edvaniq.Services.Assessment.Infrastructure;
using KnowledgeInfra = Edvaniq.Services.Knowledge.Infrastructure;
using LearningEngineInfra = Edvaniq.Services.LearningEngine.Infrastructure;
using PlanningInfra = Edvaniq.Services.Planning.Infrastructure;

namespace Edvaniq.EventFlowTests;

// Knowledge and its three receivers in one process, each with its own database and user, all on the same NATS. The
// receivers know nothing of each other, only the broker connects them.
public sealed class FlowServices(MySqlTestServer mysql, NatsTestServer nats) : IAsyncLifetime
{
    private readonly List<IAsyncDisposable> hosts = [];

    public WebApplicationFactory<knowledge::Program> Knowledge { get; private set; } = null!;

    public IReadOnlyList<Receiver> Receivers { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        Knowledge = await StartAsync<knowledge::Program, KnowledgeInfra.KnowledgeDbContext>(KnowledgeInfra.InfrastructureExtensions.DatabaseName);
        var planningHost = await StartAsync<planning::Program, PlanningInfra.PlanningDbContext>(PlanningInfra.InfrastructureExtensions.DatabaseName);
        var assessmentHost = await StartAsync<assessment::Program, AssessmentInfra.AssessmentDbContext>(AssessmentInfra.InfrastructureExtensions.DatabaseName);
        var learningEngineHost = await StartAsync<learningengine::Program, LearningEngineInfra.LearningEngineDbContext>(LearningEngineInfra.InfrastructureExtensions.DatabaseName);

        Receivers =
        [
            Receiver.For<PlanningInfra.PlanningDbContext>(
                PlanningInfra.InfrastructureExtensions.ServiceKey, planningHost.Services,
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.TopicId).ToList(),
                (db, user) => db.TopicsSnapshots.IgnoreQueryFilters().Where(snapshot => snapshot.OwnerId == user).Select(snapshot => (DateTime?)snapshot.ChangedAt).SingleOrDefault()),
            Receiver.For<AssessmentInfra.AssessmentDbContext>(
                AssessmentInfra.InfrastructureExtensions.ServiceKey, assessmentHost.Services,
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.TopicId).ToList(),
                (db, user) => db.TopicsSnapshots.IgnoreQueryFilters().Where(snapshot => snapshot.OwnerId == user).Select(snapshot => (DateTime?)snapshot.ChangedAt).SingleOrDefault()),
            Receiver.For<LearningEngineInfra.LearningEngineDbContext>(
                LearningEngineInfra.InfrastructureExtensions.ServiceKey, learningEngineHost.Services,
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.TopicId).ToList(),
                (db, user) => db.TopicsSnapshots.IgnoreQueryFilters().Where(snapshot => snapshot.OwnerId == user).Select(snapshot => (DateTime?)snapshot.ChangedAt).SingleOrDefault()),
        ];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in hosts)
        {
            await host.DisposeAsync();
        }
    }

    // The broker as Knowledge sees it, to send what another service would.
    public IMessageTransport Broker => Knowledge.Services.GetRequiredService<IMessageTransport>();

    public T QueryKnowledge<T>(Func<KnowledgeInfra.KnowledgeDbContext, T> query) => Query(Knowledge.Services, query);

    private async Task<WebApplicationFactory<TEntryPoint>> StartAsync<TEntryPoint, TContext>(string database)
        where TEntryPoint : class
        where TContext : DbContext
    {
        var connectionString = await mysql.CreateDatabaseAsync(database);
        var host = new ServiceHost<TEntryPoint>(new Dictionary<string, string>
        {
            [$"ConnectionStrings:{database}"] = connectionString,
            [$"ConnectionStrings:{MessagingExtensions.NatsConnectionName}"] = nats.Url,
        });
        hosts.Add(host);

        // The same synchronous call the migrate step makes. Services starts the host with the new database.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TContext>().Database.Migrate();
        return host;
    }

    internal static T Query<TContext, T>(IServiceProvider services, Func<TContext, T> query)
        where TContext : DbContext
    {
        using var scope = services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<TContext>());
    }
}

// What a test needs to know of a receiver: which topics of a user it knows, which state it took last, and how many
// messages its TopicsChanged consumer has handled.
public sealed record Receiver(
    string Name,
    Func<string, IReadOnlyList<Guid>> KnownTopics,
    Func<string, DateTime?> ChangedAt,
    Func<Guid, int> InboxEntries)
{
    public static Receiver For<TContext>(
        string service,
        IServiceProvider services,
        Func<TContext, string, List<Guid>> knownTopics,
        Func<TContext, string, DateTime?> changedAt)
        where TContext : DbContext
    {
        var consumer = $"{service}-TopicsChangedHandler";
        return new(
            service,
            user => FlowServices.Query<TContext, List<Guid>>(services, db => knownTopics(db, user)),
            user => FlowServices.Query<TContext, DateTime?>(services, db => changedAt(db, user)),
            messageId => FlowServices.Query<TContext, int>(
                services, db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == consumer)));
    }

    public override string ToString() => Name;
}