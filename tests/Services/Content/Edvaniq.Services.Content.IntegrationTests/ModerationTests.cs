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
using Edvaniq.Services.Content.UnitTests;
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
public sealed partial class ModerationTests(ServiceFactory service) : IClassFixture<ServiceFactory>, IDisposable
{
    private const string Lesson = """
        # Fotosynthese
        Licht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff.

        # Zellatmung
        Zucker wird in den Mitochondrien zu Energie für die Zelle.
        """;

    private readonly IHost ai = SimulatedAi();

    private readonly List<string> asked = [];

    // The task whose answers get a marker, as if the AI had written something unfit.
    private string? marking;

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
        // The material, the topics, then the check of the topics.
        Assert.Equal([ModerationCheck.Task, ContentAi.TopicsTask, ModerationCheck.Task], asked);
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

    // AC2: topics the AI answers with something unfit are not reported, the document fails.
    [Fact]
    public async Task FlaggedTopics_AreNotReported()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, Lesson);
        marking = ContentAi.TopicsTask;

        await AnalyzeAsync(id);

        Assert.Equal((DocumentStatus.Failed, FailureCode.Inappropriate), (Stored(id).Status, Stored(id).FailureCode));
        Assert.Empty(await ReportedAsync(id));
    }

    // AC2: unfit questions never go out, neither does any question of the document.
    [Fact]
    public async Task FlaggedQuestions_AreNotSent()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, Lesson);
        await AnalyzeAsync(id);
        service.Knowledge.TakeOver(Assert.Single(await ReportedAsync(id)));
        marking = ContentAi.QuestionsTask;

        await Pools().FinishAsync(id, Cancel);

        Assert.Equal((DocumentStatus.Failed, FailureCode.Inappropriate), (Stored(id).Status, Stored(id).FailureCode));
        Assert.Empty(await OutboxAsync<ItemsGenerated>(id));
        Assert.Empty(Query(db => db.QuestionPools.IgnoreQueryFilters().Where(pool => pool.DocumentId == id).ToList()));
    }

    // AC2: an unfit summary is not kept, the user reads why.
    [Fact]
    public async Task FlaggedSummary_IsNotKept()
    {
        var alice = NewUser();
        using var client = ClientFor(alice);
        var id = await PasteAsync(client, Lesson);
        await AnalyzeAsync(id);
        var topic = service.Knowledge.TakeOver(Assert.Single(await ReportedAsync(id))).First().Id;
        var sections = Query(db => db.Sections.IgnoreQueryFilters().Where(section => section.DocumentId == id).Select(section => section.Id).ToList());
        service.Material.Give(alice, topic, [.. sections.Select(section => (id, section))]);
        using (var requested = await client.PostAsync($"/topics/{topic}/summaries/short", null, Cancel))
        {
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        }

        marking = ContentAi.SummaryTask;
        await ActivatorUtilities.CreateInstance<SummaryWriter>(service.Services, Chat()).WriteWaitingAsync(Cancel);

        var summary = Query(db => db.TopicSummaries.IgnoreQueryFilters().AsNoTracking().Single(item => item.TopicId == topic));
        Assert.Equal((SummaryStatus.Failed, (string?)null), (summary.Status, summary.Text));
        Assert.Equal(DocumentFailures.SummaryInappropriate.Text, summary.FailureReason);
    }

    // #328: the injection of the reference set is school material with instructions in it. The check passes it like
    // any other, and topics and questions are checked and go on without anything the injection asks for. The check
    // itself guards against unfit content, not injection: the classifier reads the material as it is.
    [Fact]
    public async Task InjectionOfTheReferenceSet_PassesTheCheck_AndGoesOnUnchanged()
    {
        var reference = Assert.Single(ReferenceSet.Load().Documents, document => document.Kind == "injection");
        var forbidden = reference.Forbidden!;
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client, reference.Text);

        await AnalyzeAsync(id);
        var topics = Assert.Single(await ReportedAsync(id)).Topics;
        service.Knowledge.TakeOver(Assert.Single(await ReportedAsync(id)));
        await Pools().FinishAsync(id, Cancel);

        Assert.Equal(DocumentStatus.Ready, Stored(id).Status);
        Assert.Equal(
            [ModerationCheck.Task, ContentAi.TopicsTask, ModerationCheck.Task],
            asked.Take(3));
        Assert.Contains(ContentAi.QuestionsTask, asked);
        Assert.Equal(ModerationCheck.Task, asked[^1]);
        Assert.All(topics, topic => Assert.DoesNotContain(forbidden, word => topic.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        var questions = (await OutboxAsync<ItemsGenerated>(id)).SelectMany(generated => generated.Items).Select(item => item.Question!).ToList();
        Assert.NotEmpty(questions);
        Assert.All(questions, question => Assert.All(
            question.Options.Append(question.Text).Append(question.Explanation),
            text => Assert.DoesNotContain(forbidden, word => text.Contains(word, StringComparison.OrdinalIgnoreCase))));
    }

    public void Dispose() => ai.Dispose();

    private QuestionPools Pools()
    {
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(service.Services, Chat());
        pools.OutageDelays = [];
        return pools;
    }

    private IChatClient Chat() => new Marking(new Recording(Simulated, asked), () => marking);

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    private async Task AnalyzeAsync(Guid id)
    {
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        await Analyzer([]).AnalyzeAsync(id, Cancel);
    }

    private DocumentAnalyzer Analyzer(IReadOnlyList<TimeSpan> delays)
    {
        var analyzer = ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, Chat());
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

    private Task<List<DocumentAnalyzed>> ReportedAsync(Guid id) => OutboxAsync<DocumentAnalyzed>(id);

    private async Task<List<T>> OutboxAsync<T>(Guid id)
    {
        using var scope = service.Services.CreateScope();
        var subject = EventRoute.For(typeof(T)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject && message.Payload.Contains(id.ToString()))
            .Select(message => message.Payload)
            .ToListAsync(Cancel);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<T>(payload, JsonSerializerOptions.Web)!)];
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

    // Puts the marker of sexual content into the first text of the answers to the task, as if the AI had written it.
    private sealed partial class Marking(IChatClient inner, Func<string?> task) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            var response = await base.GetResponseAsync(list, options, cancellationToken);
            var system = list.FirstOrDefault(message => message.Role == ChatRole.System)?.Text ?? "";
            if (task() is not { } marked || !system.StartsWith(marked, StringComparison.Ordinal))
            {
                return response;
            }

            var text = FirstText().Replace(response.Text, $"$1{SimulatedModeration.Marker("S12")} ", 1);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = response.ModelId, Usage = response.Usage };
        }

        [System.Text.RegularExpressions.GeneratedRegex("(\"(?:title|text|summary)\"\\s*:\\s*\")")]
        private static partial System.Text.RegularExpressions.Regex FirstText();
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