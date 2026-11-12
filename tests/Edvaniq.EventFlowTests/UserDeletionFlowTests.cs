using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.EventFlowTests;

// The way of a deleted user (#201): Identity sends UserDeleted, and Planning, Content, Knowledge, Assessment and
// LearningEngine each remove every row of the user, in every table with an owner. Another user keeps everything, and
// the same deletion a second time changes nothing.
[Collection(FlowCollection.Name)]
public sealed class UserDeletionFlowTests(FlowServices services) : IDisposable
{
    private const string Text = """
        # Photosynthese
        ## Lichtreaktion
        Licht spaltet Wasser.

        # Zellatmung
        Zucker wird zu Energie.
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IHost ai = SimulatedAi();

    [Fact(Timeout = 240_000)]
    public async Task DeletedUser_HasNoRowLeftInAnyService_OthersKeepTheirs()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var bob = $"bob-{Guid.NewGuid():N}";
        await LearnAsync(alice);
        await LearnAsync(bob);
        var ofBob = Rows(bob);
        Assert.All(ofBob, service => Assert.True(service.Value > 0, $"{service.Key} has nothing of Bob to keep."));
        Assert.All(Rows(alice), service => Assert.True(service.Value > 0, $"{service.Key} has nothing of Alice to delete."));

        await PublishAsync(new UserDeleted(alice), Guid.NewGuid());

        await WaitForAsync(() => Task.FromResult(Rows(alice).Values.All(count => count == 0)), "A service kept rows of the deleted user.");
        Assert.Equal(ofBob, Rows(bob));

        await PublishAsync(new UserDeleted(alice), Guid.NewGuid());
        await PublishAsync(new UserDeleted(bob[..^1]), Guid.NewGuid());
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.All(Rows(alice).Values, count => Assert.Equal(0, count));
        Assert.Equal(ofBob, Rows(bob));
    }

    // #944: events of the user that come after the deletion, over the real broker, create nothing in any service. Bob's
    // events behind them in the same streams show that they went through.
    [Fact(Timeout = 240_000)]
    public async Task LateEvents_OfADeletedUser_CreateNothing()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var alice = $"alice-{Guid.NewGuid():N}";
        var bob = $"bob-{Guid.NewGuid():N}";
        await PublishAsync(new UserDeleted(alice), Guid.NewGuid());
        var hash = DeletedUser.HashOf(alice);
        await WaitForAsync(
            () => Task.FromResult(DeletedEverywhere(hash)),
            "Not every service remembers the deletion.");

        await PublishAsync("knowledge", new TopicsChanged(alice, Guid.NewGuid(), DateTime.UtcNow, [Guid.NewGuid()], []), cancellation);
        await PublishAsync("content", Analysis(alice), cancellation);
        await PublishAsync("knowledge", new TopicsChanged(bob, Guid.NewGuid(), DateTime.UtcNow, [Guid.NewGuid()], []), cancellation);
        await PublishAsync("content", Analysis(bob), cancellation);

        await WaitForAsync(
            () => Task.FromResult(KnowsTopicsOf(bob)),
            "Bob's events did not arrive.");
        Assert.All(Rows(alice), service => Assert.Equal(0, service.Value));
    }

    private bool DeletedEverywhere(string hash) =>
        services.QueryPlanning(db => db.Set<DeletedUser>().Any(user => user.Hash == hash))
        && services.QueryContent(db => db.Set<DeletedUser>().Any(user => user.Hash == hash))
        && services.QueryKnowledge(db => db.Set<DeletedUser>().Any(user => user.Hash == hash))
        && services.QueryAssessment(db => db.Set<DeletedUser>().Any(user => user.Hash == hash))
        && services.QueryLearningEngine(db => db.Set<DeletedUser>().Any(user => user.Hash == hash));

    // Planning, Assessment and LearningEngine took Bob's state, Knowledge his analysis.
    private bool KnowsTopicsOf(string user) =>
        services.QueryPlanning(db => db.KnownTopics.IgnoreQueryFilters().Any(topic => topic.OwnerId == user))
        && services.QueryAssessment(db => db.KnownTopics.IgnoreQueryFilters().Any(topic => topic.OwnerId == user))
        && services.QueryLearningEngine(db => db.KnownTopics.IgnoreQueryFilters().Any(topic => topic.OwnerId == user))
        && services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters().Any(topic => topic.OwnerId == user));

    private static DocumentAnalyzed Analysis(string user) =>
        new(user, Guid.NewGuid(), [new("Photosynthese", null, [Guid.NewGuid()])]);

    // Into the sender's stream, like its dispatcher would.
    private async Task PublishAsync<TEvent>(string sender, TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        await services.Broker.EnsureStreamAsync(
            new StreamSpec(EventRoute.StreamOf(sender), EventRoute.SubjectsOf(sender)), cancellationToken);
        await services.Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(TEvent)).Subject, Guid.NewGuid(), JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web)),
            cancellationToken);
    }

    // #953: within a minute of UserDeleted every service turns the deleted account's token away, over the real broker.
    // Another account's token keeps working.
    [Fact(Timeout = 120_000)]
    public async Task TokenOfADeletedAccount_IsRejectedEverywhere_WithinAMinute()
    {
        var aliceId = $"alice-{Guid.NewGuid():N}";
        var alice = TestTokens.Create(aliceId);
        var bob = TestTokens.Create($"bob-{Guid.NewGuid():N}");
        Assert.All(await MeEverywhereAsync(alice), status => Assert.Equal(HttpStatusCode.OK, status.Value));

        await PublishAsync(new UserDeleted(aliceId), Guid.NewGuid());

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        Dictionary<string, HttpStatusCode> statuses;
        while ((statuses = await MeEverywhereAsync(alice)).Values.Any(status => status != HttpStatusCode.Unauthorized))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Still accepted after a minute: {string.Join(", ", statuses.Where(s => s.Value != HttpStatusCode.Unauthorized).Select(s => s.Key))}");
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

        Assert.All(await MeEverywhereAsync(bob), status => Assert.Equal(HttpStatusCode.OK, status.Value));
    }

    private async Task<Dictionary<string, HttpStatusCode>> MeEverywhereAsync(string token) => new()
    {
        ["planning"] = await MeAsync(services.Planning.CreateClient(), token),
        ["content"] = await MeAsync(services.Content.CreateClient(), token),
        ["knowledge"] = await MeAsync(services.Knowledge.CreateClient(), token),
        ["assessment"] = await MeAsync(services.Assessment.CreateClient(), token),
        ["learningengine"] = await MeAsync(services.LearningEngine.CreateClient(), token),
    };

    private static async Task<HttpStatusCode> MeAsync(HttpClient client, string token)
    {
        using var authorized = Client(client, token);
        using var response = await authorized.GetAsync("/me", TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    public void Dispose() => ai.Dispose();

    private IChatClient Chat => ai.Services.GetRequiredService<IChatClient>();

    // Content's AI on the simulated provider.
    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
        builder.AddContentAi();
        return builder.Build();
    }

    // Leaves data of the user in every service: a learning goal, a document with its topics and questions, and an
    // answer with its learning record.
    private async Task LearnAsync(string user)
    {
        var token = TestTokens.Create(user);
        Guid documentId;
        using (var content = Client(services.Content.CreateClient(), token))
        {
            using var response = await content.PostAsJsonAsync("/documents", new { text = Text }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            documentId = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        }

        await ActivatorUtilities.CreateInstance<DocumentSplitter>(services.Content.Services).SplitAsync(documentId, TestContext.Current.CancellationToken);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(services.Content.Services, Chat).AnalyzeAsync(documentId, TestContext.Current.CancellationToken);
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(services.Content.Services, Chat);
        await WaitForAsync(
            async () => await pools.FinishAsync(documentId, TestContext.Current.CancellationToken),
            "The worker never got the topics from Knowledge.");

        var topic = services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters().First(item => item.DocumentId == documentId && item.ParentId == null).Id);
        await FlowServices.WaitForAsync(
            () => Task.FromResult(services.QueryAssessment(db => db.Questions.IgnoreQueryFilters().Any(question => question.OwnerId == user && question.TopicId == topic))),
            () => $"Assessment did not take the questions over. {services.QuestionsOf(documentId, user)}",
            Timeout);
        using var assessment = Client(services.Assessment.CreateClient(), token);
        using var asked = await assessment.PostAsJsonAsync("/attempts", new { topicId = topic }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var attempt = (await asked.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        using var graded = await assessment.PostAsJsonAsync(
            $"/attempts/{attempt}/answer", new { showSolution = true, responseTimeMs = 5000 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, graded.StatusCode);
        await WaitForAsync(
            () => Task.FromResult(services.QueryLearningEngine(db => db.LearningRecords.IgnoreQueryFilters().Any(record => record.OwnerId == user))),
            "LearningEngine has no learning record of the answer.");
    }

    // Into Identity's stream, like its dispatcher would.
    private async Task PublishAsync(UserDeleted deleted, Guid messageId)
    {
        await services.Broker.EnsureStreamAsync(
            new StreamSpec(EventRoute.StreamOf("identity"), EventRoute.SubjectsOf("identity")), TestContext.Current.CancellationToken);
        await services.Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(UserDeleted)).Subject, messageId, JsonSerializer.Serialize(deleted, JsonSerializerOptions.Web)),
            TestContext.Current.CancellationToken);
    }

    // The rows of the user per service, over every table with an owner.
    private Dictionary<string, int> Rows(string user) => new()
    {
        ["planning"] = services.QueryPlanning(db => OwnedRows(db, user)),
        ["content"] = services.QueryContent(db => OwnedRows(db, user)),
        ["knowledge"] = services.QueryKnowledge(db => OwnedRows(db, user)),
        ["assessment"] = services.QueryAssessment(db => OwnedRows(db, user)),
        ["learningengine"] = services.QueryLearningEngine(db => OwnedRows(db, user)),
    };

    private static int OwnedRows(DbContext db, string user) =>
        db.Model.GetEntityTypes()
            .Select(entity => entity.ClrType)
            .Where(typeof(IOwnedByUser).IsAssignableFrom)
            .Sum(type => (int)typeof(UserDeletionFlowTests)
                .GetMethod(nameof(Count), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type)
                .Invoke(null, [db, user])!);

    private static int Count<T>(DbContext db, string user) where T : class, IOwnedByUser =>
        db.Set<T>().IgnoreQueryFilters().Count(row => row.OwnerId == user);

    private static HttpClient Client(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Consumers, dispatchers and the worker run in the background.
    private static async Task WaitForAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }
}