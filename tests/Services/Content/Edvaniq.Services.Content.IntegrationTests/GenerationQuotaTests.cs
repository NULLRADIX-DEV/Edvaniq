using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.SharedKernel;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// #302: every topic that gets a question pool is one generation of the user's day (Content:GenerationsPerDay, 2 here).
// A document that needs more than are left fails before the first question is asked, with the time the quota starts
// again, and works after 04:00 in Berlin. A document with more topics than a whole day allows goes on a day nothing was
// used yet and takes the whole day. Each test has its own users.
public sealed class GenerationQuotaTests(GenerationQuotaFactory service) : IClassFixture<GenerationQuotaFactory>, IDisposable
{
    private readonly IHost ai = SimulatedAi();

    private readonly List<string> asked = [];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => ai.Dispose();

    [Fact]
    public async Task DocumentOverTheRest_FailsBeforeTheQuestions_AndWorksAfterTheReset()
    {
        using var client = ClientFor(NewUser());
        await AnalyzeAsync(await PasteAsync(client, Topics("Photosynthese")));
        var second = await PasteAsync(client, Topics("Zellatmung", "Genetik"));
        asked.Clear();

        await AnalyzeAsync(second);

        var now = service.Time.GetUtcNow().UtcDateTime;
        var failed = Stored(second);
        Assert.Equal((DocumentStatus.Failed, GenerationQuota.TooMany(2, 1, now)), (failed.Status, failed.FailureReason));
        Assert.DoesNotContain(asked, prompt => prompt.StartsWith(ContentAi.QuestionsTask, StringComparison.Ordinal));
        Assert.Equal(0, Query(db => db.GenerationCharges.IgnoreQueryFilters().Count(charge => charge.DocumentId == second)));
        Assert.Equal(0, Query(db => db.QuestionPools.IgnoreQueryFilters().Count(pool => pool.DocumentId == second)));

        service.Time.Advance(StudyDay.NextStartUtc(now) - now);
        using var retry = await client.PostAsync($"/documents/{second}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        await AnalyzeAsync(second);

        Assert.Equal(DocumentStatus.Ready, Stored(second).Status);
        Assert.Equal(2, Query(db => db.GenerationCharges.IgnoreQueryFilters().Count(charge => charge.DocumentId == second)));
        Assert.Equal((0, 2), await QuotaAsync(client));
    }

    // A document with more topics than the day allows goes on a fresh day and takes the whole day, the next is turned
    // away in the Api.
    [Fact]
    public async Task BigDocument_OnAFreshDay_TakesTheWholeDay()
    {
        using var client = ClientFor(NewUser());
        var big = await PasteAsync(client, Topics("Photosynthese", "Zellatmung", "Genetik"));

        await AnalyzeAsync(big);

        Assert.Equal(DocumentStatus.Ready, Stored(big).Status);
        Assert.Equal(3, Query(db => db.GenerationCharges.IgnoreQueryFilters().Count(charge => charge.DocumentId == big)));
        Assert.Equal((0, 2), await QuotaAsync(client));

        using var next = await client.PostAsJsonAsync("/documents", new { text = Topics("Evolution") }, Cancel);
        Assert.Equal(HttpStatusCode.TooManyRequests, next.StatusCode);
        var problem = await next.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(GenerationQuota.UsedUp(service.Time.GetUtcNow().UtcDateTime), problem.GetProperty("title").GetString());
        Assert.Equal(StudyDay.NextStartUtc(service.Time.GetUtcNow().UtcDateTime), problem.GetProperty("resetsAt").GetDateTime().ToUniversalTime());
    }

    // GET /quota counts the caller's own generations only.
    [Fact]
    public async Task Quota_ShowsTheCallersOwnGenerations()
    {
        using var alice = ClientFor(NewUser());
        using var bob = ClientFor(NewUser());
        await AnalyzeAsync(await PasteAsync(alice, Topics("Photosynthese")));

        Assert.Equal((1, 2), await QuotaAsync(alice));
        Assert.Equal((2, 2), await QuotaAsync(bob));
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    // One topic per heading for the simulated analysis, each with its own section.
    private static string Topics(params string[] names) =>
        string.Join("\n\n", names.Select(name => $"# {name}\n{TestTexts.Lesson}"));

    // As the worker does it: split, topics, Knowledge takes them over, then the question pools.
    private async Task AnalyzeAsync(Guid id)
    {
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        var analyzer = ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, Chat());
        analyzer.OutageDelays = [];
        await analyzer.AnalyzeAsync(id, Cancel);
        service.Knowledge.TakeOver((await AnalyzedAsync(id))[^1]);
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(service.Services, Chat());
        pools.OutageDelays = [];
        await pools.FinishAsync(id, Cancel);
    }

    private async Task<List<DocumentAnalyzed>> AnalyzedAsync(Guid documentId)
    {
        using var scope = service.Services.CreateScope();
        var subject = EventRoute.For(typeof(DocumentAnalyzed)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject && message.Payload.Contains(documentId.ToString()))
            .OrderBy(message => message.Id)
            .Select(message => message.Payload)
            .ToListAsync(Cancel);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<DocumentAnalyzed>(payload, JsonSerializerOptions.Web)!)];
    }

    private IChatClient Chat() => new SystemPrompts(ai.Services.GetRequiredService<IChatClient>(), asked);

    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
        builder.AddContentAi();
        return builder.Build();
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Guid> PasteAsync(HttpClient client, string text)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<(int Left, int PerDay)> QuotaAsync(HttpClient client)
    {
        var quota = await client.GetFromJsonAsync<JsonElement>("/quota", Cancel);
        return (quota.GetProperty("generationsLeftToday").GetInt32(), quota.GetProperty("generationsPerDay").GetInt32());
    }

    private Document Stored(Guid id) => Query(db => db.Documents.IgnoreQueryFilters().AsNoTracking().Single(document => document.Id == id));

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    // Keeps the system prompt of every request that reaches the AI.
    private sealed class SystemPrompts(IChatClient inner, List<string> prompts) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            lock (prompts)
            {
                prompts.AddRange(messages.Where(message => message.Role == ChatRole.System).Select(message => message.Text));
            }

            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }
}

// The service with 2 generations a day and a clock the tests move.
public sealed class GenerationQuotaFactory(MySqlTestServer mysql) : ServiceFactory(mysql)
{
    public TestTime Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Content:GenerationsPerDay", "2")
            .ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }
}