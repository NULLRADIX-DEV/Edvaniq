using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Api;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #411, A93: the demo learning goal gets its material, topics, question pools and summaries without a single AI call:
// the configured AI throws on every call and the cost log stays empty. One demo per user, only for accounts with the
// right to it, in a goal of the user's own, and deleting it takes its documents with everything made of them.
public sealed class DemoTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Demo_IsAnalyzed_FromItsAnswers_WithoutAnyAiCall()
    {
        var alice = NewUser();
        using var client = ClientFor(alice, testDouble: true);
        var ai = new NoAi();

        var demo = await CreateAsync(client, alice, HttpStatusCode.Created);
        Assert.Equal(DemoMaterial.Documents.Select(document => document.Title), demo.Documents.Select(document => document.Title));
        Assert.All(demo.Documents, document => Assert.Equal("demo", document.Origin));
        Assert.Equal(service.Planning.GoalOf(alice), demo.LearningGoalId);

        var topics = await AnalyzeAsync(demo, ai);

        Assert.Equal(DemoReplay.Topics.Count, topics.Count);
        foreach (var document in demo.Documents)
        {
            var stored = Query(db => db.Documents.IgnoreQueryFilters().AsNoTracking().Single(item => item.Id == document.Id));
            Assert.Equal((DocumentStatus.Ready, DocumentOrigin.Demo), (stored.Status, stored.Origin));
            Assert.All(await OutboxAsync<ItemsGenerated>(document.Id), items => Assert.True(items.Items.Count >= QuestionPool.MinQuestions));
        }

        var topic = topics[0];
        service.Material.Give(alice, topic.Id, [.. topic.SectionIds.Select(section => (demo.Documents[0].Id, section))]);
        using (var requested = await client.PostAsync($"/topics/{topic.Id}/summaries/standard", null, Cancel))
        {
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        }

        await ActivatorUtilities.CreateInstance<SummaryWriter>(service.Services, (IChatClient)ai).WriteWaitingAsync(Cancel);
        Assert.Equal(SummaryStatus.Ready, Query(db => db.TopicSummaries.IgnoreQueryFilters().Single(summary => summary.TopicId == topic.Id)).Status);

        var ids = demo.Documents.Select(document => document.Id).ToList();
        Assert.Equal(0, ai.Calls);
        Assert.False(Query(db => db.AiAttempts.IgnoreQueryFilters().Any(attempt => ids.Contains(attempt.DocumentId))));
        Assert.False(Query(db => db.GenerationCharges.IgnoreQueryFilters().Any(charge => ids.Contains(charge.DocumentId))));
    }

    [Fact]
    public async Task SecondRequest_GivesTheSameDemo_AndGetFindsIt()
    {
        var alice = NewUser();
        using var client = ClientFor(alice, testDouble: true);
        using (var none = await client.GetAsync("/demo", Cancel))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        }

        var first = await CreateAsync(client, alice, HttpStatusCode.Created);
        var second = await CreateAsync(client, alice, HttpStatusCode.OK);
        var found = await client.GetFromJsonAsync<DemoDto>("/demo", Cancel);

        Assert.Equal(first.Documents.Select(document => document.Id), second.Documents.Select(document => document.Id));
        Assert.Equal(first.Documents.Select(document => document.Id), found!.Documents.Select(document => document.Id));
        Assert.Equal(DemoMaterial.Documents.Count, Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == alice)));
    }

    // Until #414: test accounts and the accounts the operator names. A goal that isn't the user's is none.
    [Fact]
    public async Task OnlyAllowedAccounts_InTheirOwnGoal_GetADemo()
    {
        var bob = NewUser();
        var carol = NewUser();
        using var plain = ClientFor(bob, testDouble: false);
        using var foreign = ClientFor(NewUser(), testDouble: true);
        await using var named = service.WithWebHostBuilder(builder => builder.UseSetting("Demo:Accounts:0", carol));
        using var carols = named.CreateClient();
        carols.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(carol));

        using var refused = await plain.PostAsJsonAsync("/demo", new { learningGoalId = service.Planning.GoalOf(bob) }, Cancel);
        using var otherGoal = await foreign.PostAsJsonAsync("/demo", new { learningGoalId = service.Planning.GoalOf(bob) }, Cancel);
        using var allowed = await carols.PostAsJsonAsync("/demo", new { learningGoalId = service.Planning.GoalOf(carol) }, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(DemoEndpoints.NotAllowed, (await refused.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, otherGoal.StatusCode);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.False(Query(db => db.Documents.IgnoreQueryFilters().Any(document => document.OwnerId == bob)));

        // #414: resetting needs the right as well.
        using var reset = await plain.DeleteAsync("/demo", Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        using var resetAgain = await plain.PostAsync("/demo/reset", null, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, resetAgain.StatusCode);
    }

    // #412: back to the start in the same goal. Only the demo documents go, each with DocumentDeleted, so the other
    // services drop what came of them; the user's own material stays.
    [Fact]
    public async Task Reset_PutsFreshDemoDocuments_IntoTheSameGoal_OthersStay()
    {
        var alice = NewUser();
        using var client = ClientFor(alice, testDouble: true);
        using (var none = await client.PostAsync("/demo/reset", null, Cancel))
        {
            Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        }

        var first = await CreateAsync(client, alice, HttpStatusCode.Created);
        using (var pasted = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson, confirmation = "material-1" }, Cancel))
        {
            Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
        }

        using var reset = await client.PostAsync("/demo/reset", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var second = (await reset.Content.ReadFromJsonAsync<DemoDto>(Cancel))!;
        await AnalyzeAsync(second, new NoAi());

        Assert.Equal(first.LearningGoalId, second.LearningGoalId);
        Assert.Equal(DemoMaterial.Documents.Select(document => document.Title), second.Documents.Select(document => document.Title));
        Assert.Empty(first.Documents.Select(document => document.Id).Intersect(second.Documents.Select(document => document.Id)));
        foreach (var document in first.Documents)
        {
            Assert.Single(await OutboxAsync<DocumentDeleted>(document.Id));
        }

        Assert.All(second.Documents, document =>
            Assert.Equal(DocumentStatus.Ready, Query(db => db.Documents.IgnoreQueryFilters().Single(item => item.Id == document.Id)).Status));
        Assert.Equal(1, Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == alice && document.Origin == DocumentOrigin.Material)));
        Assert.Equal(DemoMaterial.Documents.Count, Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == alice && document.Origin == DocumentOrigin.Demo)));
        Assert.Equal(second.Documents.Select(document => document.Id), (await client.GetFromJsonAsync<DemoDto>("/demo", Cancel))!.Documents.Select(document => document.Id));
    }

    // #414: requests at the same moment make one demo, the others get it back.
    [Fact]
    public async Task RequestsAtTheSameTime_MakeOneDemo()
    {
        var alice = NewUser();
        var clients = Enumerable.Range(0, 4).Select(_ => ClientFor(alice, testDouble: true)).ToList();

        var responses = await Task.WhenAll(clients.Select(client =>
            client.PostAsJsonAsync("/demo", new { learningGoalId = service.Planning.GoalOf(alice) }, Cancel)));
        var demos = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<DemoDto>(Cancel)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response => Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, response.StatusCode.ToString()));
        Assert.All(demos, demo => Assert.Equal(demos[0]!.Documents.Select(document => document.Id), demo!.Documents.Select(document => document.Id)));
        Assert.Equal(DemoMaterial.Documents.Count, Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == alice)));
        Assert.Equal(1, Query(db => db.DemoGoals.IgnoreQueryFilters().Count(goal => goal.OwnerId == alice)));
        foreach (var item in responses.Cast<IDisposable>().Concat(clients))
        {
            item.Dispose();
        }
    }

    // #414: a reset in the same goal makes new documents, which get ready like the first ones did: nothing of the
    // deleted documents' ids is in the way of the new events.
    [Fact]
    public async Task ResetInTheSameGoal_MakesNewDocuments_ThatGetReady()
    {
        var alice = NewUser();
        using var client = ClientFor(alice, testDouble: true);
        var first = await CreateAsync(client, alice, HttpStatusCode.Created);
        using (var deleted = await client.DeleteAsync("/demo", Cancel))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        var second = await CreateAsync(client, alice, HttpStatusCode.Created);
        await AnalyzeAsync(second, new NoAi());

        Assert.Equal(first.LearningGoalId, second.LearningGoalId);
        Assert.Empty(first.Documents.Select(document => document.Id).Intersect(second.Documents.Select(document => document.Id)));
        Assert.All(second.Documents, document =>
            Assert.Equal(DocumentStatus.Ready, Query(db => db.Documents.IgnoreQueryFilters().Single(item => item.Id == document.Id)).Status));
    }

    // The learning goal stays with Planning until it can be deleted (#519).
    [Fact]
    public async Task Delete_TakesTheDemoDocuments_OthersStay()
    {
        var alice = NewUser();
        using var client = ClientFor(alice, testDouble: true);
        var demo = await CreateAsync(client, alice, HttpStatusCode.Created);
        using (var pasted = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson, confirmation = "material-1" }, Cancel))
        {
            Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
        }

        using var deleted = await client.DeleteAsync("/demo", Cancel);
        using var again = await client.DeleteAsync("/demo", Cancel);
        using var gone = await client.GetAsync("/demo", Cancel);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (deleted.StatusCode, again.StatusCode, gone.StatusCode));
        Assert.Equal(DocumentOrigin.Material, Query(db => db.Documents.IgnoreQueryFilters().Single(document => document.OwnerId == alice)).Origin);
        foreach (var document in demo.Documents)
        {
            Assert.Single(await OutboxAsync<DocumentDeleted>(document.Id));
        }
    }

    // The worker's steps for each demo document: split, analyze, Knowledge takes the topics over, the pools follow.
    private async Task<List<KnowledgeTopic>> AnalyzeAsync(DemoDto demo, NoAi ai)
    {
        var topics = new List<KnowledgeTopic>();
        foreach (var document in demo.Documents)
        {
            await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(document.Id, Cancel);
            await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, (IChatClient)ai).AnalyzeAsync(document.Id, Cancel);
            topics.AddRange(service.Knowledge.TakeOver(Assert.Single(await OutboxAsync<DocumentAnalyzed>(document.Id))));
            Assert.True(await ActivatorUtilities.CreateInstance<QuestionPools>(service.Services, (IChatClient)ai).FinishAsync(document.Id, Cancel));
        }

        return topics;
    }

    private async Task<DemoDto> CreateAsync(HttpClient client, string user, HttpStatusCode status)
    {
        using var response = await client.PostAsJsonAsync("/demo", new { learningGoalId = service.Planning.GoalOf(user) }, Cancel);
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DemoDto>(Cancel))!;
    }

    private async Task<List<T>> OutboxAsync<T>(Guid documentId)
    {
        using var scope = service.Services.CreateScope();
        var subject = EventRoute.For(typeof(T)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject && message.Payload.Contains(documentId.ToString()))
            .Select(message => message.Payload)
            .ToListAsync(Cancel);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<T>(payload, JsonSerializerOptions.Web)!)];
    }

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    private HttpClient ClientFor(string userId, bool testDouble)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            testDouble ? TestTokens.Create(userId, claims: [new Claim("test_double", "true", ClaimValueTypes.Boolean)]) : TestTokens.Create(userId));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private sealed record DemoDto(Guid LearningGoalId, DocumentDto[] Documents);

    private sealed record DocumentDto(Guid Id, string Title, string Status, string Origin);

    // The configured AI of the worker. The demo must never get here.
    private sealed class NoAi : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("The demo asked the AI.");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}