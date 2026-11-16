using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Application;
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

// #339: a pool left with fewer than 15 questions after the check is topped up by the worker, with questions it hasn't
// sent before, within the user's daily quota. While the quota is used up, the budget reached or the AI stopped, nothing
// is asked and the log says why. An outage waits and tries again later, the document stays ready all along. One class
// with its own database: the emergency stop and the budget are one row for the whole service.
public sealed class QuestionTopUpTests(QuestionTopUpFactory service) : IClassFixture<QuestionTopUpFactory>, IAsyncLifetime
{
    private readonly IHost ai = SimulatedAi();

    private readonly Script script = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    // Every test leaves the AI running and without a budget.
    public async ValueTask DisposeAsync()
    {
        using var scope = service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAiSwitch>().SetAsync(false, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IAiBudget>().SetAsync(null, CancellationToken.None);
        ai.Dispose();
    }

    // Round one gets five again and two new ones, only the two go out. Round two gets the simulated 15 to 20 and fills
    // the pool up to 20. Every round asks for the missing ones and takes one generation.
    [Fact]
    public async Task ShortPool_IsToppedUp_WithOnlyNewQuestions_UpToTwenty()
    {
        script.Answers.Enqueue(Questions("Erste", 5));
        script.Answers.Enqueue(Questions("Erste", 5, "Neue", 2));
        using var client = ClientFor(NewUser());
        var (document, topic) = await PooledAsync(client);

        Assert.True(await TopUpAsync(topic));
        Assert.True(await TopUpAsync(topic));

        var sent = await GeneratedAsync(document);
        Assert.Equal([5, 2, 13], sent.Select(generated => generated.Items.Count));
        Assert.All(sent, generated => Assert.Equal(topic, generated.TopicId));
        Assert.Equal(["Neue 1", "Neue 2"], sent[1].Items.Select(item => item.Question!.Text));
        var texts = sent.SelectMany(generated => generated.Items).Select(item => QuestionGeneration.SameQuestion(item.Question!.Text)).ToList();
        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.All(sent[2].Items, item => Assert.Equal(QuestionGeneration.PromptVersion, item.PromptVersion));

        var pool = Pool(topic);
        Assert.Equal((20, 2, false), (pool.Questions, pool.TopUps, pool.WantsTopUp));
        Assert.Equal([0, 1, 2], Query(db => db.GenerationCharges.IgnoreQueryFilters().Where(charge => charge.TopicId == topic).Select(charge => charge.Round).Order().ToList()));
        Assert.Contains(script.Prompts, prompt => prompt.Contains("Zu diesem Thema gibt es schon 5 Fragen. Schreib 15 weitere", StringComparison.Ordinal));
        Assert.Contains(script.Prompts, prompt => prompt.Contains("Zu diesem Thema gibt es schon 7 Fragen. Schreib 13 weitere", StringComparison.Ordinal));
        Assert.Equal(DocumentStatus.Ready, Stored(document).Status);
        Assert.False(await DueAsync(topic));
    }

    // #427: a top-up from an AI that repeats its questions sends each new one once and still fills the pool.
    [Fact]
    public async Task RepeatedQuestions_InATopUp_AreSentOnce()
    {
        script.Answers.Enqueue(Questions("Erste", 5));
        using var client = ClientFor(NewUser());
        var (document, topic) = await PooledAsync(client);
        var simulated = ai.Services.GetRequiredService<SimulatedChatClient>();
        simulated.Fault = SimulatedFault.DuplicateItems;
        simulated.FaultWhen = prompt => prompt.System.StartsWith(ContentAi.QuestionsTask, StringComparison.Ordinal);

        Assert.True(await TopUpAsync(topic));

        var sent = await GeneratedAsync(document);
        Assert.Equal([5, 15], sent.Select(generated => generated.Items.Count));
        var texts = sent.SelectMany(generated => generated.Items).Select(item => QuestionGeneration.SameQuestion(item.Question!.Text)).ToList();
        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.Equal(20, Pool(topic).Questions);
    }

    // An AI that brings nothing new three times leaves the pool as it is, with a log line, and Assessment gets nothing
    // more.
    [Fact]
    public async Task ThreeRoundsWithoutEnough_LeaveThePool_WithALogLine()
    {
        for (var round = 0; round < 4; round++)
        {
            script.Answers.Enqueue(Questions("Immer", 5));
        }

        using var client = ClientFor(NewUser());
        var (document, topic) = await PooledAsync(client);

        for (var round = 0; round < QuestionPool.MaxTopUps + 1; round++)
        {
            await TopUpAsync(topic);
        }

        Assert.Single(await GeneratedAsync(document));
        var pool = Pool(topic);
        Assert.Equal((5, QuestionPool.MaxTopUps, true), (pool.Questions, pool.TopUps, pool.TopUpStopped));
        Assert.Single(service.Logs.Entries, entry => entry.EventName == "TopUpStayedShort"
            && entry.Message == $"Question pool of topic {topic} stays at 5 questions after 3 top-up rounds");
    }

    // A used-up quota asks nothing until the quota starts again, then the pool fills.
    [Fact]
    public async Task UsedUpQuota_AsksNothing_UntilTheReset()
    {
        script.Answers.Enqueue(Questions("Erste", 5));
        using var client = ClientFor(NewUser());
        var (document, topic) = await PooledAsync(client);
        UseUpTheDay(document);
        var asked = script.Prompts.Count;

        Assert.True(await TopUpAsync(topic));

        var now = service.Time.GetUtcNow().UtcDateTime;
        Assert.Equal(asked, script.Prompts.Count);
        Assert.Equal(StudyDay.NextStartUtc(now), Pool(topic).NextTopUpAt);
        Assert.Single(service.Logs.Entries, entry => entry.EventName == "TopUpOverQuota" && entry.Message.Contains(topic.ToString(), StringComparison.Ordinal));
        Assert.False(await DueAsync(topic));

        service.Time.Advance(StudyDay.NextStartUtc(now) - now);
        Assert.True(await TopUpAsync(topic));

        Assert.Equal(20, Pool(topic).Questions);
    }

    // Stopped by the operator: no call, a log line, and another try later, which works once the AI runs again.
    [Fact]
    public async Task StoppedAi_AsksNothing_AndTriesAgainLater()
    {
        script.Answers.Enqueue(Questions("Erste", 5));
        using var client = ClientFor(NewUser());
        var (_, topic) = await PooledAsync(client);
        await SwitchAsync(paused: true);
        var asked = script.Prompts.Count;

        Assert.True(await TopUpAsync(topic));

        Assert.Equal(asked, script.Prompts.Count);
        var now = service.Time.GetUtcNow().UtcDateTime;
        Assert.Equal((5, now + QuestionTopUps.PausedDelay), (Pool(topic).Questions ?? 0, Pool(topic).NextTopUpAt!.Value));
        Assert.Single(service.Logs.Entries, entry => entry.EventName == "TopUpPaused" && entry.Message.Contains(topic.ToString(), StringComparison.Ordinal));
        Assert.False(await DueAsync(topic));

        await SwitchAsync(paused: false);
        service.Time.Advance(QuestionTopUps.PausedDelay);
        Assert.True(await TopUpAsync(topic));

        Assert.Equal(20, Pool(topic).Questions);
    }

    // An outage waits by the delays and then tries again, the document stays ready and readable meanwhile.
    [Fact]
    public async Task Outage_WaitsAndTriesAgain_WhileTheDocumentStaysReady()
    {
        script.Answers.Enqueue(Questions("Erste", 5));
        script.Outages = 1;
        using var client = ClientFor(NewUser());
        var (document, topic) = await PooledAsync(client);

        Assert.True(await TopUpAsync(topic));

        var pool = Pool(topic);
        Assert.Equal((5, 1, service.Time.GetUtcNow().UtcDateTime + Document.OutageDelays[0]), (pool.Questions ?? 0, pool.TopUpOutages, pool.NextTopUpAt!.Value));
        Assert.Single(service.Logs.Entries, entry => entry.EventName == "TopUpPostponed" && entry.Message.Contains(topic.ToString(), StringComparison.Ordinal));
        using var read = await client.GetAsync($"/documents/{document}", Cancel);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.False(await DueAsync(topic));

        service.Time.Advance(Document.OutageDelays[0]);
        Assert.True(await TopUpAsync(topic));

        Assert.Equal((20, 0), (Pool(topic).Questions, Pool(topic).TopUpOutages));
        Assert.Equal(DocumentStatus.Ready, Stored(document).Status);
    }

    // A deleted document takes its pool with it, nothing is topped up for it.
    [Fact]
    public async Task DeletedDocument_IsNotToppedUp()
    {
        script.Answers.Enqueue(Questions("Erste", 5));
        using var client = ClientFor(NewUser());
        var (document, topic) = await PooledAsync(client);
        using var deleted = await client.DeleteAsync($"/documents/{document}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var asked = script.Prompts.Count;

        Assert.False(await DueAsync(topic));

        Assert.Equal(asked, script.Prompts.Count);
        Assert.Equal(0, Query(db => db.QuestionPools.IgnoreQueryFilters().Count(pool => pool.TopicId == topic)));
    }

    // A pool from before #339 doesn't know how many it has and is left as it is.
    [Fact]
    public async Task PoolFromBefore_IsNotToppedUp()
    {
        script.Answers.Enqueue(Questions("Erste", 5));
        using var client = ClientFor(NewUser());
        var (_, topic) = await PooledAsync(client);
        await using (var scope = service.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ContentDbContext>().QuestionPools.IgnoreQueryFilters()
                .Where(pool => pool.TopicId == topic)
                .ExecuteUpdateAsync(pool => pool.SetProperty(item => item.Questions, (int?)null).SetProperty(item => item.QuestionKeys, (string?)null), Cancel);
        }

        Assert.False(await DueAsync(topic));
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    // A ready document with one topic and its first pool.
    private async Task<(Guid Document, Guid Topic)> PooledAsync(HttpClient client)
    {
        var id = await PasteAsync(client, $"# Licht\n{TestTexts.Lesson}");
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        var analyzer = ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, Chat());
        analyzer.OutageDelays = [];
        await analyzer.AnalyzeAsync(id, Cancel);
        var topic = Assert.Single(service.Knowledge.TakeOver(Assert.Single(await OutboxAsync<DocumentAnalyzed>(id)))).Id;
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(service.Services, Chat());
        pools.OutageDelays = [];
        Assert.True(await pools.FinishAsync(id, Cancel));
        Assert.Equal(DocumentStatus.Ready, Stored(id).Status);
        return (id, topic);
    }

    // A round of the worker for this test's pool, if the round would take it on: the other pools belong to other tests,
    // which share the clock. True if it was worked on.
    private async Task<bool> TopUpAsync(Guid topic) => await DueAsync(topic) && await TopUps().TopUpAsync(topic, Cancel);

    private async Task<bool> DueAsync(Guid topic) => (await TopUps().WaitingAsync(Cancel)).Contains(topic);

    private QuestionTopUps TopUps()
    {
        var topUps = ActivatorUtilities.CreateInstance<QuestionTopUps>(service.Services, Chat());
        topUps.AiTimeout = TimeSpan.FromSeconds(5);
        topUps.Batch = 1000;
        return topUps;
    }

    private async Task SwitchAsync(bool paused)
    {
        await using var scope = service.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAiSwitch>().SetAsync(paused, Cancel);
    }

    // Every day's generation of the user taken by charges of other topics.
    private void UseUpTheDay(Guid documentId)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = db.Documents.IgnoreQueryFilters().Single(item => item.Id == documentId);
        var now = service.Time.GetUtcNow().UtcDateTime;
        for (var charge = 1; charge < QuestionTopUpFactory.GenerationsPerDay; charge++)
        {
            db.GenerationCharges.Add(GenerationCharge.For(document, Guid.NewGuid(), now));
        }

        db.SaveChanges();
    }

    // A question per name and number, "Erste 1" to "Erste 5", in the form of questions-4.
    private static string Questions(params object[] groups)
    {
        var questions = new List<object>();
        for (var group = 0; group < groups.Length; group += 2)
        {
            for (var number = 1; number <= (int)groups[group + 1]; number++)
            {
                questions.Add(new
                {
                    type = "single",
                    text = $"{groups[group]} {number}",
                    options = new[] { "A", "B" },
                    answers = new[] { 0 },
                    explanation = "Weil.",
                    difficulty = 1,
                    section = 1,
                });
            }
        }

        // The first round asks for cards as well (#340), top-ups leave them out.
        return JsonSerializer.Serialize(new { questions, cards = new[] { new { front = "Karte", back = "Rückseite", section = 1 } } }, JsonSerializerOptions.Web);
    }

    // The questions only, the cards of a new pool (#340) left out.
    private async Task<List<ItemsGenerated>> GeneratedAsync(Guid documentId) =>
        [.. (await OutboxAsync<ItemsGenerated>(documentId)).Select(generated => generated with { Items = [.. generated.Items.Where(item => item.Kind == ItemKind.Question)] })];

    private async Task<List<T>> OutboxAsync<T>(Guid documentId)
    {
        using var scope = service.Services.CreateScope();
        var subject = EventRoute.For(typeof(T)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject && message.Payload.Contains(documentId.ToString()))
            .OrderBy(message => message.Id)
            .Select(message => message.Payload)
            .ToListAsync(Cancel);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<T>(payload, JsonSerializerOptions.Web)!)];
    }

    private IChatClient Chat()
    {
        script.InnerClient = ai.Services.GetRequiredService<IChatClient>();
        return script;
    }

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
        using var response = await client.PostAsJsonAsync("/documents", new { text, confirmation = "material-1" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private QuestionPool Pool(Guid topicId) =>
        Query(db => db.QuestionPools.IgnoreQueryFilters().AsNoTracking().Single(pool => pool.TopicId == topicId));

    private Document Stored(Guid id) => Query(db => db.Documents.IgnoreQueryFilters().AsNoTracking().Single(document => document.Id == id));

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    // Answers the question prompts from the queue while it lasts, the first Outages of them with an outage instead, and
    // leaves everything else to the simulated AI. Keeps every system prompt for questions.
    private sealed class Script : IChatClient
    {
        public IChatClient InnerClient { get; set; } = null!;

        public Queue<string> Answers { get; } = new();

        public List<string> Prompts { get; } = [];

        public int Outages { get; set; }

        private int calls;

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            if (!list[0].Text.StartsWith(ContentAi.QuestionsTask, StringComparison.Ordinal))
            {
                return await InnerClient.GetResponseAsync(list, options, cancellationToken);
            }

            lock (Prompts)
            {
                Prompts.Add(list[0].Text);
            }

            // The first question prompt is the new pool, the outages come with the top-ups.
            if (++calls > 1 && Outages > 0)
            {
                Outages--;
                throw new HttpRequestException("The test AI is down.", null, HttpStatusCode.ServiceUnavailable);
            }

            return Answers.TryDequeue(out var answer)
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
                : await InnerClient.GetResponseAsync(list, options, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => InnerClient.GetService(serviceType, serviceKey);

        public void Dispose()
        {
        }
    }
}

// The service with a clock the tests move and a quota that is easy to use up.
public sealed class QuestionTopUpFactory(MySqlTestServer mysql) : ServiceFactory(mysql)
{
    public const int GenerationsPerDay = 10;

    public TestTime Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Content:GenerationsPerDay", GenerationsPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }
}