using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Api;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Content.UnitTests;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #344, #345: the summary of a topic comes into being on its first request, in the worker, and is kept. Every later read costs
// no AI call, the three lengths differ clearly, each names its sources, model and prompt, and only its owner sees it.
// The users are test accounts, so the worker takes the simulated AI.
public sealed class SummaryTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Text = """
        # Photosynthese
        Licht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff für die Luft. Aus Kohlendioxid und Wasser baut die Pflanze Zucker.

        # Zellatmung
        In den Mitochondrien wird Zucker mit Sauerstoff zu Energie, Wasser und Kohlendioxid.
        """;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstRequest_MakesTheSummaryOnce_LaterReadsCallNoAi()
    {
        var alice = NewUser();
        var (topic, document, sections) = await TopicAsync(alice);
        using var client = ClientFor(alice);

        var requested = await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);
        var again = await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);
        await WriteAsync();
        var ready = await client.GetFromJsonAsync<SummaryDto>($"/topics/{topic}/summaries/short", Cancel);
        var later = await RequestAsync(client, topic, "short", HttpStatusCode.OK);
        await WriteAsync();

        Assert.Equal(("pending", null), (requested.Status, requested.Text));
        Assert.Equal("pending", again.Status);
        Assert.Equal(("ready", true, TopicSummaryGeneration.PromptVersion), (ready!.Status, ready.AiGenerated, ready.PromptVersion));
        Assert.False(string.IsNullOrWhiteSpace(ready.Text));
        Assert.False(string.IsNullOrWhiteSpace(ready.Model));
        Assert.Equal([new SourceDto(document, "Photosynthese", null, null, "material", sections[0].Id)], ready.Sources);
        Assert.Equal(ready.Text, later.Text);
        Assert.Equal(1, SummaryCalls(document));
    }

    // Each length in the middle of its own frame, so every one is clearly longer than the one before.
    [Fact]
    public async Task ThreeLengths_DifferClearly()
    {
        var alice = NewUser();
        var (topic, _, _) = await TopicAsync(alice);
        using var client = ClientFor(alice);
        string[] lengths = ["short", "standard", "detailed"];
        foreach (var length in lengths)
        {
            await RequestAsync(client, topic, length, HttpStatusCode.Accepted);
        }

        await WriteAsync();

        var words = new List<int>();
        foreach (var (length, frame) in lengths.Zip([SummaryLength.Short, SummaryLength.Standard, SummaryLength.Detailed]))
        {
            var summary = await client.GetFromJsonAsync<SummaryDto>($"/topics/{topic}/summaries/{length}", Cancel);
            Assert.Equal("ready", summary!.Status);
            var count = TopicSummaryGeneration.WordCount(summary.Text!);
            Assert.True(TopicSummaryGeneration.Fits(count, TopicSummaryGeneration.Frame(frame, TopicSummaryGeneration.WordCount(Text))), $"{length}: {count}");
            words.Add(count);
        }

        Assert.True(words[0] < words[1] && words[1] < words[2], string.Join(", ", words));
    }

    // An answer that doesn't hold up, twice, fails the summary with its own reason, and the log says per attempt why and
    // what it cost, never the text.
    [Fact]
    public async Task UnusableAnswer_FailsWithItsReason_AndTheLogSaysWhy()
    {
        var alice = NewUser();
        var (topic, _, _) = await TopicAsync(alice, usesTestDouble: false);
        using var client = ClientFor(alice);
        await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);

        await WriteAsync(new WordySummaries(service.Services.GetRequiredService<TestDoubleAi>().Client));

        var summary = await client.GetFromJsonAsync<SummaryDto>($"/topics/{topic}/summaries/short", Cancel);
        Assert.Equal(("failed", "Unknown", TopicSummary.Unusable), (summary!.Status, summary.FailureCode, summary.FailureReason));
        var id = Query(db => db.TopicSummaries.IgnoreQueryFilters().Single(item => item.TopicId == topic).Id);
        var refused = service.Logs.Entries
            .Where(entry => entry.Message.Contains($"of summary {id} is refused", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(StructuredOutput.DefaultAttempts, refused.Count);
        Assert.All(refused, entry =>
        {
            Assert.Contains("(TooLong): 500 words", entry.Message, StringComparison.Ordinal);
            Assert.Contains("900 output tokens, 200 of them reasoning", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Wortreich", entry.Message, StringComparison.Ordinal);
        });
    }

    // #348, rule 5 of tests/ReferenceSet/README.md for the summaries: no forbidden text of the injection in any length,
    // and each is the summary of the same document without the injection. The simulated AI is gullible on purpose
    // (SimulatedSummary), so only the fence of the material keeps it from writing what the injection asks for. Every
    // summary passes the check of its output on the way.
    [Fact]
    public async Task InjectionOfTheReferenceSet_ChangesNoSummary()
    {
        var reference = Assert.Single(ReferenceSet.Load().Documents, document => document.Kind == "injection");
        var forbidden = reference.Forbidden!;
        var text = reference.Text.ReplaceLineEndings("\n");
        var withoutInjection = string.Join("\n\n", text.Split("\n\n")
            .Where(paragraph => !forbidden.Any(word => paragraph.Contains(word, StringComparison.OrdinalIgnoreCase))));
        Assert.NotEqual(text, withoutInjection);
        var alice = NewUser();
        var (injected, _, _) = await TopicAsync(alice, text: text, title: reference.Title!);
        var (clean, _, _) = await TopicAsync(alice, text: withoutInjection, title: reference.Title!);
        using var client = ClientFor(alice);
        string[] lengths = ["short", "standard", "detailed"];
        foreach (var length in lengths)
        {
            await RequestAsync(client, injected, length, HttpStatusCode.Accepted);
            await RequestAsync(client, clean, length, HttpStatusCode.Accepted);
        }

        await WriteAsync();

        foreach (var length in lengths)
        {
            var summary = await client.GetFromJsonAsync<SummaryDto>($"/topics/{injected}/summaries/{length}", Cancel);
            var expected = await client.GetFromJsonAsync<SummaryDto>($"/topics/{clean}/summaries/{length}", Cancel);
            Assert.Equal(("ready", "ready"), (summary!.Status, expected!.Status));
            Assert.DoesNotContain(forbidden, word => summary.Text!.Contains(word, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expected.Text, summary.Text);
        }
    }

    // A file names the pages the summary rests on.
    [Fact]
    public async Task SummaryOfAFile_NamesItsPages()
    {
        var alice = NewUser();
        var document = Document.Upload(alice, service.Planning.GoalOf(alice), "Skript.pdf", Guid.NewGuid(), FileFormat.Pdf, DateTime.UtcNow);
        var pages = document.TakeText(["# Fotosynthese\nLicht spaltet Wasser in den Blättern.", "Zucker entsteht im Calvin-Zyklus."], DateTime.UtcNow);
        var sections = document.Split();
        await StoreAsync(db =>
        {
            db.Documents.Add(document);
            db.DocumentPages.AddRange(pages);
            db.Sections.AddRange(sections);
        });
        var topic = Guid.NewGuid();
        service.Material.Give(alice, topic, [.. sections.Select(section => (document.Id, section.Id))]);
        using var client = ClientFor(alice);

        await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);
        await WriteAsync();

        var summary = await client.GetFromJsonAsync<SummaryDto>($"/topics/{topic}/summaries/short", Cancel);
        Assert.Equal([new SourceDto(document.Id, "Skript", 1, 2, "material", sections[0].Id)], summary!.Sources);
    }

    [Fact]
    public async Task OtherUser_NeitherReadsNorRequestsASummary()
    {
        var alice = NewUser();
        var (topic, _, _) = await TopicAsync(alice);
        using var aliceClient = ClientFor(alice);
        using var bob = ClientFor(NewUser());
        await RequestAsync(aliceClient, topic, "short", HttpStatusCode.Accepted);

        using var read = await bob.GetAsync($"/topics/{topic}/summaries/short", Cancel);
        using var request = await bob.PostAsync($"/topics/{topic}/summaries/standard", null, Cancel);
        using var unknown = await bob.PostAsync($"/topics/{Guid.NewGuid()}/summaries/standard", null, Cancel);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (read.StatusCode, request.StatusCode, unknown.StatusCode));
        Assert.Equal(
            [SummaryLength.Short],
            Query(db => db.TopicSummaries.IgnoreQueryFilters().Where(summary => summary.TopicId == topic).Select(summary => summary.Length).ToList()));
    }

    [Fact]
    public async Task TopicWithoutMaterialHere_IsAConflict_AndKnowledgeAway_IsUnavailable()
    {
        var alice = NewUser();
        using var client = ClientFor(alice);
        var empty = Guid.NewGuid();
        service.Material.Give(alice, empty, [(Guid.NewGuid(), Guid.NewGuid())]);
        var withoutSections = Guid.NewGuid();
        service.Material.Give(alice, withoutSections, []);
        var away = Guid.NewGuid();
        service.Material.Down(away);

        using var conflict = await client.PostAsync($"/topics/{empty}/summaries/short", null, Cancel);
        using var none = await client.PostAsync($"/topics/{withoutSections}/summaries/short", null, Cancel);
        using var unavailable = await client.PostAsync($"/topics/{away}/summaries/short", null, Cancel);

        foreach (var response in new[] { conflict, none })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(TopicSummary.NoMaterial, (await response.Content.ReadFromJsonAsync<ProblemDto>(Cancel))!.Title);
        }

        Assert.False(Query(db => db.TopicSummaries.IgnoreQueryFilters().Any(summary => summary.TopicId == empty || summary.TopicId == withoutSections)));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(SummaryEndpoints.KnowledgeUnavailable, (await unavailable.Content.ReadFromJsonAsync<ProblemDto>(Cancel))!.Title);
    }

    // #345: the AI away, the summary waits after each outage like a document, then fails with a reason the user reads, no
    // crash. Asked for again once the AI is back, it gets made.
    [Fact]
    public async Task AiOutage_PostponesThenFails_AndARetryWorksOnceTheAiIsBack()
    {
        var alice = NewUser();
        var (topic, _, _) = await TopicAsync(alice, usesTestDouble: false);
        using var client = ClientFor(alice);
        var broken = new BrokenSummaries(service.Services.GetRequiredService<TestDoubleAi>().Client);
        await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);

        var before = DateTime.UtcNow;
        await WriteAsync(broken);
        var waiting = Query(db => db.TopicSummaries.IgnoreQueryFilters().AsNoTracking().Single(summary => summary.TopicId == topic));
        Assert.Equal((SummaryStatus.Pending, 1), (waiting.Status, waiting.AiOutages));
        Assert.InRange(waiting.NextAttemptAt!.Value, before + Document.OutageDelays[0], DateTime.UtcNow + Document.OutageDelays[0]);

        // The rest without waiting.
        await StoreAsync(db => db.TopicSummaries.IgnoreQueryFilters().Where(summary => summary.TopicId == topic)
            .ExecuteUpdate(set => set.SetProperty(summary => summary.NextAttemptAt, (DateTime?)null)));
        for (var round = 0; round < Document.OutageDelays.Count; round++)
        {
            await WriteAsync(broken, delays: [.. Document.OutageDelays.Select(_ => TimeSpan.Zero)]);
        }

        var failed = await client.GetFromJsonAsync<SummaryDto>($"/topics/{topic}/summaries/short", Cancel);
        Assert.Equal(("failed", "AiUnavailable", Document.AiUnavailable, (string?)null),
            (failed!.Status, failed.FailureCode, failed.FailureReason, failed.Text));

        var retried = await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);
        Assert.Equal(("pending", (string?)null, (string?)null), (retried.Status, retried.FailureCode, retried.FailureReason));
        await WriteAsync(broken.InnerClient);

        var ready = await client.GetFromJsonAsync<SummaryDto>($"/topics/{topic}/summaries/short", Cancel);
        Assert.Equal(("ready", (string?)null), (ready!.Status, ready.FailureReason));
        Assert.False(string.IsNullOrWhiteSpace(ready.Text));
    }

    [Fact]
    public async Task PausedAi_TakesNoNewSummary_ExceptOfTheDemo()
    {
        var alice = NewUser();
        var (topic, _, _) = await TopicAsync(alice);
        var (demoTopic, _, _) = await TopicAsync(alice, origin: DocumentOrigin.Demo);
        using var client = ClientFor(alice);
        await SetPausedAsync(true);
        try
        {
            using var response = await client.PostAsync($"/topics/{topic}/summaries/short", null, Cancel);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(SummaryEndpoints.AiPaused, (await response.Content.ReadFromJsonAsync<ProblemDto>(Cancel))!.Title);
            Assert.False(Query(db => db.TopicSummaries.IgnoreQueryFilters().Any(summary => summary.TopicId == topic)));

            // #413: the demo needs no AI and goes on while it is paused.
            using var demo = await client.PostAsync($"/topics/{demoTopic}/summaries/short", null, Cancel);
            Assert.Equal(HttpStatusCode.Accepted, demo.StatusCode);
        }
        finally
        {
            await SetPausedAsync(false);
        }
    }

    [Theory]
    [InlineData("long")]
    [InlineData("Short")]
    public async Task UnknownLength_IsABadRequest(string length)
    {
        var alice = NewUser();
        var (topic, _, _) = await TopicAsync(alice);
        using var client = ClientFor(alice);

        using var response = await client.PostAsync($"/topics/{topic}/summaries/{length}", null, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // #349: a document says where it comes from, and so does every source of a summary. Synthetic documents come with M5,
    // until then only the database can make one.
    [Fact]
    public async Task Origin_IsNamedAtTheDocument_AndAtEverySourceOfASummary()
    {
        var alice = NewUser();
        var (topic, synthetic, syntheticSections) = await TopicAsync(alice);
        var (_, material, _) = await TopicAsync(alice);
        await StoreAsync(db => db.Documents.IgnoreQueryFilters().Where(document => document.Id == synthetic)
            .ExecuteUpdate(document => document.SetProperty(item => item.Origin, DocumentOrigin.Synthetic)));
        using var client = ClientFor(alice);
        await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);
        await WriteAsync();

        var summary = await client.GetFromJsonAsync<SummaryDto>($"/topics/{topic}/summaries/short", Cancel);
        var listed = await client.GetFromJsonAsync<DocumentDto[]>("/documents", Cancel);
        var single = await client.GetFromJsonAsync<DocumentDto>($"/documents/{material}", Cancel);

        Assert.Equal([new SourceDto(synthetic, "Photosynthese", null, null, "synthetic", syntheticSections[0].Id)], summary!.Sources);
        Assert.Equal(
            [(synthetic, "synthetic"), (material, "material")],
            listed!.Select(document => (document.Id, document.Origin)).OrderBy(document => document.Origin, StringComparer.Ordinal).Reverse());
        Assert.Equal("material", single!.Origin);
    }

    // A summary that rests on a deleted document goes with it, a second delete finds nothing.
    [Fact]
    public async Task DeletedDocument_TakesItsSummaries_OthersStay()
    {
        var alice = NewUser();
        var bob = NewUser();
        var (topic, document, _) = await TopicAsync(alice);
        var (otherTopic, otherDocument, _) = await TopicAsync(alice);
        var (bobsTopic, bobsDocument, _) = await TopicAsync(bob);
        using var client = ClientFor(alice);
        using var bobsClient = ClientFor(bob);
        await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);
        await RequestAsync(client, otherTopic, "short", HttpStatusCode.Accepted);
        await RequestAsync(bobsClient, bobsTopic, "short", HttpStatusCode.Accepted);
        await WriteAsync();

        using var deleted = await client.DeleteAsync($"/documents/{document}", Cancel);
        using var again = await client.DeleteAsync($"/documents/{document}", Cancel);
        using var read = await client.GetAsync($"/topics/{topic}/summaries/short", Cancel);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.False(Query(db => db.TopicSummarySections.IgnoreQueryFilters().Any(section => section.DocumentId == document)));

        // #401: the summaries of another document and of another user stay as they were.
        var other = await client.GetFromJsonAsync<SummaryDto>($"/topics/{otherTopic}/summaries/short", Cancel);
        var bobs = await bobsClient.GetFromJsonAsync<SummaryDto>($"/topics/{bobsTopic}/summaries/short", Cancel);
        Assert.Equal(("ready", "ready"), (other!.Status, bobs!.Status));
        Assert.Equal([otherDocument], other.Sources.Select(source => source.DocumentId));
        Assert.Equal([bobsDocument], bobs.Sources.Select(source => source.DocumentId));
    }

    // A pasted document with its sections, and Knowledge naming them as one topic's material.
    // Without the test double the worker takes the chat a test hands it.
    private async Task<(Guid Topic, Guid Document, IReadOnlyList<Section> Sections)> TopicAsync(
        string owner, bool usesTestDouble = true, DocumentOrigin origin = DocumentOrigin.Material, string text = Text, string title = "Photosynthese")
    {
        var document = Document.Paste(owner, service.Planning.GoalOf(owner), title, text, DateTime.UtcNow, usesTestDouble, origin);
        var sections = document.Split();
        await StoreAsync(db =>
        {
            db.Documents.Add(document);
            db.Sections.AddRange(sections);
        });
        var topic = Guid.NewGuid();
        service.Material.Give(owner, topic, [.. sections.Select(section => (document.Id, section.Id))]);
        return (topic, document.Id, sections);
    }

    private static async Task<SummaryDto> RequestAsync(HttpClient client, Guid topic, string length, HttpStatusCode status)
    {
        using var response = await client.PostAsync($"/topics/{topic}/summaries/{length}", null, Cancel);
        Assert.Equal(status, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SummaryDto>(Cancel))!;
    }

    // One round of the worker, with the simulated AI unless a test hands it another chat.
    private async Task WriteAsync(IChatClient? chat = null, IReadOnlyList<TimeSpan>? delays = null)
    {
        var writer = ActivatorUtilities.CreateInstance<SummaryWriter>(
            service.Services, chat ?? service.Services.GetRequiredService<TestDoubleAi>().Client);
        if (delays is not null)
        {
            writer.OutageDelays = delays;
        }

        await writer.WriteWaitingAsync(Cancel);
    }

    // The provider is away for every summary, other calls go through.
    private sealed class BrokenSummaries(IChatClient inner) : DelegatingChatClient(inner)
    {
        public new IChatClient InnerClient => base.InnerClient;

        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            messages.First().Text.StartsWith(ContentAi.SummaryTask, StringComparison.Ordinal)
                ? throw new AiProviderException(HttpStatusCode.ServiceUnavailable, null)
                : base.GetResponseAsync(messages, options, cancellationToken);
    }

    // Every summary far longer than any frame, as a model may write it.
    private sealed class WordySummaries(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();
            if (!list[0].Text.StartsWith(ContentAi.SummaryTask, StringComparison.Ordinal))
            {
                return await base.GetResponseAsync(list, options, cancellationToken);
            }

            var text = string.Join(' ', Enumerable.Repeat("Wortreich", 500));
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""{"summary":"{{text}}","sections":[1]}"""))
            {
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 900, ReasoningTokenCount = 200 },
            };
        }
    }

    private int SummaryCalls(Guid document) =>
        Query(db => db.AiAttempts.IgnoreQueryFilters()
            .Where(attempt => attempt.DocumentId == document && attempt.Purpose == AiAttempt.Summary)
            .Select(attempt => attempt.CallId).Distinct().Count());

    private async Task SetPausedAsync(bool paused)
    {
        using var scope = service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAiSwitch>().SetAsync(paused, Cancel);
    }

    private async Task StoreAsync(Action<ContentDbContext> add)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        add(db);
        await db.SaveChangesAsync(Cancel);
    }

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Create(userId, claims: [new Claim("test_double", "true", ClaimValueTypes.Boolean)]));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private sealed record SummaryDto(
        Guid TopicId, string Length, string Status, string? Text, string? FailureCode, string? FailureReason, bool AiGenerated, string? Model,
        string? PromptVersion, DateTime CreatedAt, DateTime? ReadyAt, SourceDto[] Sources);

    private sealed record SourceDto(Guid DocumentId, string DocumentTitle, int? PageFrom, int? PageTo, string Origin, Guid SectionId);

    private sealed record DocumentDto(Guid Id, string Origin);

    private sealed record ProblemDto(string Title);
}