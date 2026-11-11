extern alias assessment;
extern alias content;
extern alias knowledge;
extern alias learningengine;
extern alias planning;

using System.Net.Http.Headers;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using AssessmentInfra = Edvaniq.Services.Assessment.Infrastructure;
using ContentInfra = Edvaniq.Services.Content.Infrastructure;
using KnowledgeInfra = Edvaniq.Services.Knowledge.Infrastructure;
using LearningEngineInfra = Edvaniq.Services.LearningEngine.Infrastructure;
using PlanningInfra = Edvaniq.Services.Planning.Infrastructure;

namespace Edvaniq.EventFlowTests;

// Content, Knowledge and Knowledge's three receivers in one process, each with its own database and user, all on the
// same NATS. The services know nothing of each other, only the broker connects them, and Content asks Knowledge over
// HTTP with its own internal token, Planning with the token of the user who pastes.
public sealed class FlowServices(MySqlTestServer mysql, NatsTestServer nats) : IAsyncLifetime
{
    private readonly List<IAsyncDisposable> hosts = [];

    private WebApplicationFactory<learningengine::Program>? learningEngine;

    public WebApplicationFactory<knowledge::Program> Knowledge { get; private set; } = null!;

    public WebApplicationFactory<content::Program> Content { get; private set; } = null!;

    public WebApplicationFactory<assessment::Program> Assessment { get; private set; } = null!;

    public WebApplicationFactory<planning::Program> Planning { get; private set; } = null!;

    public WebApplicationFactory<learningengine::Program> LearningEngine => learningEngine!;

    public IReadOnlyList<Receiver> Receivers { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        Knowledge = await StartAsync<knowledge::Program, KnowledgeInfra.KnowledgeDbContext>(KnowledgeInfra.InfrastructureExtensions.DatabaseName);
        // Planning asks the LearningEngine below for the mastery, with the token of the user whose plan it is. That host
        // starts later, so the handler is only made on the first call.
        var planningHost = await StartAsync<planning::Program, PlanningInfra.PlanningDbContext>(
            PlanningInfra.InfrastructureExtensions.DatabaseName,
            services => services.AddHttpClient<IMasterySource, PlanningInfra.LearningEngineMastery>()
                .ConfigurePrimaryHttpMessageHandler(() => learningEngine!.Server.CreateHandler()),
            new() { ["services:learningengine-api:http:0"] = "http://learningengine-api" });
        var assessmentHost = await StartAsync<assessment::Program, AssessmentInfra.AssessmentDbContext>(AssessmentInfra.InfrastructureExtensions.DatabaseName);
        Assessment = assessmentHost;

        // What the worker registers with its token, against the Knowledge above.
        var knowledge = Knowledge.CreateClient();
        knowledge.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateForClient("content"));
        // Content asks the Planning above for the default goal, through its real client with the user's token.
        Content = await StartAsync<content::Program, ContentInfra.ContentDbContext>(
            ContentInfra.InfrastructureExtensions.DatabaseName,
            services =>
            {
                services.AddSingleton<IKnowledgeTopics>(new ContentInfra.KnowledgeTopics(knowledge));
                services.AddHttpClient<Edvaniq.Services.Content.Application.ILearningGoals, ContentInfra.PlanningLearningGoals>()
                    .ConfigurePrimaryHttpMessageHandler(() => planningHost.Server.CreateHandler());
            },
            new()
            {
                ["services:planning-api:http:0"] = "http://planning-api",
                ["FileStorage:Directory"] = Path.Combine(Path.GetTempPath(), "edvaniq-flow-files", Guid.NewGuid().ToString("N")),
            });
        Planning = planningHost;
        var learningEngineHost = await StartAsync<learningengine::Program, LearningEngineInfra.LearningEngineDbContext>(LearningEngineInfra.InfrastructureExtensions.DatabaseName);
        learningEngine = learningEngineHost;

        Receivers =
        [
            Receiver.For<PlanningInfra.PlanningDbContext>(
                PlanningInfra.InfrastructureExtensions.ServiceKey, planningHost.Services,
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.TopicId).ToList(),
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.LearningGoalId).Distinct().ToList(),
                (db, user) => db.TopicsSnapshots.IgnoreQueryFilters().Where(snapshot => snapshot.OwnerId == user).Max(snapshot => (DateTime?)snapshot.ChangedAt)),
            Receiver.For<AssessmentInfra.AssessmentDbContext>(
                AssessmentInfra.InfrastructureExtensions.ServiceKey, assessmentHost.Services,
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.TopicId).ToList(),
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.LearningGoalId).Distinct().ToList(),
                (db, user) => db.TopicsSnapshots.IgnoreQueryFilters().Where(snapshot => snapshot.OwnerId == user).Max(snapshot => (DateTime?)snapshot.ChangedAt)),
            Receiver.For<LearningEngineInfra.LearningEngineDbContext>(
                LearningEngineInfra.InfrastructureExtensions.ServiceKey, learningEngineHost.Services,
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.TopicId).ToList(),
                (db, user) => db.KnownTopics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.LearningGoalId).Distinct().ToList(),
                (db, user) => db.TopicsSnapshots.IgnoreQueryFilters().Where(snapshot => snapshot.OwnerId == user).Max(snapshot => (DateTime?)snapshot.ChangedAt)),
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

    public T QueryContent<T>(Func<ContentInfra.ContentDbContext, T> query) => Query(Content.Services, query);

    public T QueryAssessment<T>(Func<AssessmentInfra.AssessmentDbContext, T> query) => Query(Assessment.Services, query);

    public T QueryPlanning<T>(Func<PlanningInfra.PlanningDbContext, T> query) => Query(Planning.Services, query);

    public T QueryLearningEngine<T>(Func<LearningEngineInfra.LearningEngineDbContext, T> query) => Query(LearningEngine.Services, query);

    private async Task<WebApplicationFactory<TEntryPoint>> StartAsync<TEntryPoint, TContext>(
        string database, Action<IServiceCollection>? services = null, Dictionary<string, string>? settings = null)
        where TEntryPoint : class
        where TContext : DbContext
    {
        var connectionString = await mysql.CreateDatabaseAsync(database);
        settings ??= [];
        settings[$"ConnectionStrings:{database}"] = connectionString;
        settings[$"ConnectionStrings:{MessagingExtensions.NatsConnectionName}"] = nats.Url;
        var host = new ServiceHost<TEntryPoint>(settings, services);
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

// What a test needs to know of a receiver: which topics of a user it knows and in which learning goals, which state it
// took last, and how many messages its TopicsChanged consumer has handled.
public sealed record Receiver(
    string Name,
    Func<string, IReadOnlyList<Guid>> KnownTopics,
    Func<string, IReadOnlyList<Guid>> Goals,
    Func<string, DateTime?> ChangedAt,
    Func<Guid, int> InboxEntries)
{
    public static Receiver For<TContext>(
        string service,
        IServiceProvider services,
        Func<TContext, string, List<Guid>> knownTopics,
        Func<TContext, string, List<Guid>> goals,
        Func<TContext, string, DateTime?> changedAt)
        where TContext : DbContext
    {
        var consumer = $"{service}-TopicsChangedHandler";
        return new(
            service,
            user => FlowServices.Query<TContext, List<Guid>>(services, db => knownTopics(db, user)),
            user => FlowServices.Query<TContext, List<Guid>>(services, db => goals(db, user)),
            user => FlowServices.Query<TContext, DateTime?>(services, db => changedAt(db, user)),
            messageId => FlowServices.Query<TContext, int>(
                services, db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == consumer)));
    }

    public override string ToString() => Name;
}