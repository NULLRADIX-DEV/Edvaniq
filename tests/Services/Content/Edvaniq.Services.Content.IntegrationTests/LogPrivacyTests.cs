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
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// #417: the material and what the AI makes of it never reach the log, not as a message, a value or an exception. One
// word stands for both, in the material, in unusable answers and in the questions that come out. The
// way through pool, top-up and summary leaves its lines with fixed event ids and the cost of each call with its topic
// or summary.
public sealed class LogPrivacyTests(ServiceFactory service) : IClassFixture<ServiceFactory>, IDisposable
{
    private const string Marker = "Zitronenfalterxylophon";

    private const string Material = $"""
        # Mondfinsternis
        Bei einer Mondfinsternis steht die Erde zwischen Sonne und Mond. Das {Marker} klingt dann im Schatten der Erde.
        Der Mond wird dunkel und rot, weil die Atmosphäre nur rotes Licht zu ihm bricht.
        """;

    private readonly IHost ai = SimulatedAi();

    private readonly Script script = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PoolTopUpAndSummary_LogNoMaterialAndNoAnswer()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);
        var id = await PasteAsync(client);
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, Chat()).AnalyzeAsync(id, Cancel);
        var topic = Assert.Single(service.Knowledge.TakeOver(Assert.Single(await OutboxAsync<DocumentAnalyzed>(id)))).Id;

        script.Questions.Enqueue($$"""{"questions":[{"text":"{{Marker}} ohne Antworten"}]}""");
        script.Questions.Enqueue(Questions(Marker, 2));
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(service.Services, Chat());
        pools.OutageDelays = [];
        Assert.True(await pools.FinishAsync(id, Cancel));
        Assert.Equal(DocumentStatus.Ready, Query(db => db.Documents.IgnoreQueryFilters().Single(document => document.Id == id)).Status);

        script.Questions.Enqueue($"Kein JSON, nur {Marker}.");
        script.Questions.Enqueue(Questions($"{Marker}neu", 3));
        var topUps = ActivatorUtilities.CreateInstance<QuestionTopUps>(service.Services, Chat());
        topUps.Batch = 1000;
        Assert.Contains(topic, await topUps.WaitingAsync(Cancel));
        Assert.True(await topUps.TopUpAsync(topic, Cancel));

        var sections = Query(db => db.Sections.IgnoreQueryFilters().Where(section => section.DocumentId == id).Select(section => section.Id).ToList());
        service.Material.Give(alice, topic, [.. sections.Select(section => (id, section))]);
        using (var requested = await client.PostAsync($"/topics/{topic}/summaries/short", null, Cancel))
        {
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        }

        script.Summaries.Enqueue($"{Marker} ist keine Zusammenfassung.");
        await ActivatorUtilities.CreateInstance<SummaryWriter>(service.Services, Chat()).WriteWaitingAsync(Cancel);
        var summary = Query(db => db.TopicSummaries.IgnoreQueryFilters().AsNoTracking().Single(item => item.TopicId == topic));
        Assert.Equal(SummaryStatus.Ready, summary.Status);
        Assert.False(string.IsNullOrWhiteSpace(summary.Text));

        Assert.DoesNotContain(service.Logs.Entries, entry => entry.Text.Contains(Marker, StringComparison.OrdinalIgnoreCase));

        // The lines of the way, each with its fixed id, and the cost of every call with what it was about.
        var events = service.Logs.Entries.Where(entry => entry.Text.Contains(id.ToString(), StringComparison.Ordinal)
            || entry.Text.Contains(topic.ToString(), StringComparison.Ordinal)
            || entry.Text.Contains(summary.Id.ToString(), StringComparison.Ordinal)).Select(entry => entry.EventName).ToList();
        Assert.Contains("PoolsMade", events);
        Assert.Contains("ToppedUp", events);
        Assert.Contains("LogReady", events);
        Assert.Equal(
            new[]
            {
                (AiAttempt.Moderation, (Guid?)null, (Guid?)null), (AiAttempt.Questions, topic, null), (AiAttempt.TopUp, topic, null),
                (AiAttempt.Summary, null, summary.Id),
            },
            Query(db => db.AiAttempts.IgnoreQueryFilters().Where(attempt => attempt.DocumentId == id && attempt.Purpose != AiAttempt.Topics)
                .OrderBy(attempt => attempt.CalledAt)
                .Select(attempt => new { attempt.Purpose, attempt.TopicId, attempt.SummaryId })
                .ToList())
                .Select(attempt => (attempt.Purpose, attempt.TopicId, attempt.SummaryId))
                .Distinct());
        Assert.Contains(service.Logs.Entries, entry => entry.EventName == "AiUsage"
            && entry.Message.Contains($"summary {summary.Id}", StringComparison.Ordinal)
            && entry.Message.Contains($"prompt {TopicSummaryGeneration.PromptVersion}", StringComparison.Ordinal));
    }

    public void Dispose() => ai.Dispose();

    // Questions in the form of the prompt, the name in their text and their explanation.
    private static string Questions(string name, int count) => JsonSerializer.Serialize(
        new
        {
            questions = Enumerable.Range(1, count).Select(number => new
            {
                type = "single",
                text = $"Was ist {name} {number}?",
                options = new[] { "A", "B" },
                answers = new[] { 0 },
                explanation = $"Weil {name}.",
                difficulty = 1,
                section = 1,
            }),
        },
        JsonSerializerOptions.Web);

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

    private static async Task<Guid> PasteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text = Material, confirmation = "material-1" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
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

    // Answers the questions and summaries from its queues while they last, everything else the simulated AI does.
    private sealed class Script : IChatClient
    {
        public IChatClient InnerClient { get; set; } = null!;

        public Queue<string> Questions { get; } = new();

        public Queue<string> Summaries { get; } = new();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            var queue = list[0].Text.StartsWith(ContentAi.QuestionsTask, StringComparison.Ordinal) ? Questions
                : list[0].Text.StartsWith(ContentAi.SummaryTask, StringComparison.Ordinal) ? Summaries
                : null;
            return queue is not null && queue.TryDequeue(out var answer)
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