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

// #298: material is checked before any of it goes to the AI for topics. Unfit material fails with the reason of the
// fixed list, school material of every subject passes, and a check that fails holds the document back: nothing goes on
// unchecked. The simulated classifier flags what carries a marker (SimulatedModeration).
public sealed class ModerationTests(ServiceFactory service) : IClassFixture<ServiceFactory>, IDisposable
{
    private const string Lesson = """
        # Fotosynthese
        Licht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff.

        # Zellatmung
        Zucker wird in den Mitochondrien zu Energie für die Zelle.
        """;

    private readonly IHost ai = SimulatedAi();

    private readonly List<string> asked = [];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private SimulatedChatClient Simulated => ai.Services.GetRequiredService<SimulatedChatClient>();

    // AC1: unfit material fails with the reason the user reads, and nothing of it reaches the AI for topics.
    [Theory]
    [InlineData("S12")]
    [InlineData("S4")]
    [InlineData("S10")]
    public async Task FlaggedMaterial_FailsAsInappropriate_WithoutTopics(string code)
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, Lesson + $"\n\nNoch ein Absatz {SimulatedModeration.Marker(code)} am Ende.");

        await AnalyzeAsync(id);

        var found = await client.GetFromJsonAsync<JsonElement>($"/documents/{id}", Cancel);
        Assert.Equal(("Failed", "Inappropriate"), (found.GetProperty("status").GetString(), found.GetProperty("failureCode").GetString()));
        Assert.Equal(DocumentFailures.Inappropriate.Text, found.GetProperty("failureReason").GetString());
        Assert.Empty(await ReportedAsync(id));
        Assert.Equal([ModerationCheck.Task], asked);
        Assert.Contains(service.Logs.Entries, entry => entry.EventName == "ModerationFlagged" && entry.Message.EndsWith(code, StringComparison.Ordinal));
        Assert.Single(AttemptsOf(id), attempt => attempt.Purpose == AiAttempt.Moderation);
    }

    // A category that doesn't count here, like advice or privacy, stops nothing.
    [Fact]
    public async Task OtherCategory_StopsNothing()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, Lesson + $"\n\nRechtlicher Rat {SimulatedModeration.Marker("S6")} zum Mietvertrag.");

        await AnalyzeAsync(id);

        Assert.Single(await ReportedAsync(id));
    }

    // School material on hard subjects passes, the classifier judges it like any other text.
    [Theory]
    [InlineData("""
        # Fortpflanzung
        Bei der Befruchtung verschmelzen Eizelle und Spermium. Beim Geschlechtsverkehr gelangen Spermien in die Scheide.
        """)]
    [InlineData("""
        # Zweiter Weltkrieg
        Im Zweiten Weltkrieg wurden Millionen Menschen ermordet. Der Holocaust war der Völkermord an den europäischen Juden.
        """)]
    [InlineData("""
        # Psychische Gesundheit
        Suizidgedanken sind behandelbar. Wer sie hat, findet rund um die Uhr Hilfe bei der Telefonseelsorge.
        """)]
    public async Task SchoolMaterial_Passes(string text)
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, text);

        await AnalyzeAsync(id);

        Assert.Single(await ReportedAsync(id));
        Assert.Equal([ModerationCheck.Task, ContentAi.TopicsTask], asked);
    }

    // The local list catches a plain insult before any AI is asked.
    [Fact]
    public async Task InsultOfTheLocalList_FailsWithoutAskingTheAi()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, Lesson + "\n\nDer Lehrer ist ein Wichser.");

        await AnalyzeAsync(id);

        Assert.Equal(DocumentStatus.Failed, Stored(id).Status);
        Assert.Equal(FailureCode.Inappropriate, Stored(id).FailureCode);
        Assert.Empty(asked);
        Assert.Empty(AttemptsOf(id));
    }

    // AC3: a check that fails holds the document back like an outage of the AI, never lets it pass. After the last delay
    // it fails as "KI derzeit nicht verfügbar", and a retry with the check back makes it go on.
    [Fact]
    public async Task OutageOfTheCheck_HoldsTheDocumentBack_NothingGoesOn()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, Lesson);
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        Simulated.Fault = SimulatedFault.Outage;
        var analyzer = Analyzer([TimeSpan.Zero]);

        await analyzer.AnalyzeAsync(id, Cancel);

        var waiting = Stored(id);
        Assert.Equal((DocumentStatus.Analyzing, 1, (DateTime?)null), (waiting.Status, waiting.AiOutages, waiting.AnalyzedAt));
        Assert.NotNull(waiting.NextAttemptAt);

        await analyzer.AnalyzeAsync(id, Cancel);

        var failed = Stored(id);
        Assert.Equal((DocumentStatus.Failed, FailureCode.AiUnavailable), (failed.Status, failed.FailureCode));
        Assert.Empty(await ReportedAsync(id));
        Assert.DoesNotContain(ContentAi.TopicsTask, asked);
        Assert.All(AttemptsOf(id), attempt => Assert.Equal((AiAttempt.Moderation, AiAttemptOutcome.Failed), (attempt.Purpose, attempt.Outcome)));

        Simulated.Fault = SimulatedFault.None;
        using var retried = await client.PostAsync($"/documents/{id}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, retried.StatusCode);
        await AnalyzeAsync(id);

        Assert.Single(await ReportedAsync(id));
    }

    // An answer of the check that can't be read is asked again and then holds the document, never taken as safe.
    [Fact]
    public async Task UnreadableCheck_NeverPasses()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, Lesson);
        Simulated.Fault = SimulatedFault.InvalidOutput;
        Simulated.FaultWhen = prompt => prompt.Task == ModerationCheck.Task;

        await AnalyzeAsync(id);

        Assert.Equal((DocumentStatus.Failed, FailureCode.AiUnavailable), (Stored(id).Status, Stored(id).FailureCode));
        Assert.Empty(await ReportedAsync(id));
        Assert.Equal(StructuredOutput.DefaultAttempts, asked.Count(task => task == ModerationCheck.Task));
        Assert.Contains(service.Logs.Entries, entry => entry.EventName == "ModerationUnreadable");
    }

    public void Dispose() => ai.Dispose();

    private async Task AnalyzeAsync(Guid id)
    {
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        await Analyzer([]).AnalyzeAsync(id, Cancel);
    }

    private DocumentAnalyzer Analyzer(IReadOnlyList<TimeSpan> delays)
    {
        var analyzer = ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, (IChatClient)new Recording(Simulated, asked));
        analyzer.OutageDelays = delays;
        return analyzer;
    }

    private Document Stored(Guid id)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters().AsNoTracking().Single(document => document.Id == id);
    }

    private List<AiAttempt> AttemptsOf(Guid id)
    {
        using var scope = service.Services.CreateScope();
        return [.. scope.ServiceProvider.GetRequiredService<ContentDbContext>().AiAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(attempt => attempt.DocumentId == id)];
    }

    private async Task<List<DocumentAnalyzed>> ReportedAsync(Guid id)
    {
        using var scope = service.Services.CreateScope();
        var subject = EventRoute.For(typeof(DocumentAnalyzed)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject && message.Payload.Contains(id.ToString()))
            .Select(message => message.Payload)
            .ToListAsync(Cancel);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<DocumentAnalyzed>(payload, JsonSerializerOptions.Web)!)];
    }

    private static async Task<Guid> PasteAsync(HttpClient client, string text)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text, confirmation = "material-1" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
        builder.AddContentAi();
        return builder.Build();
    }

    // Keeps the task of every request: the named one, or the first line of the system prompt.
    private sealed class Recording(IChatClient inner, List<string> asked) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            lock (asked)
            {
                asked.Add(AiRequest.TaskOf(options)
                    ?? list.FirstOrDefault(message => message.Role == ChatRole.System)?.Text.Split('\n', 2)[0].Trim()
                    ?? "");
            }

            return base.GetResponseAsync(list, options, cancellationToken);
        }
    }
}