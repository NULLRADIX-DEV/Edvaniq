using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

using Edvaniq.Services.Content.Api;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #344: the summary of a topic comes into being on its first request, in the worker, and is kept. Every later read costs
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
        var (topic, document, _) = await TopicAsync(alice);
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
        Assert.Equal([new SourceDto(document, "Photosynthese", null, null)], ready.Sources);
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
            Assert.InRange(count, TopicSummaryGeneration.WordsOf(frame).Min, TopicSummaryGeneration.WordsOf(frame).Max);
            words.Add(count);
        }

        Assert.True(words[0] < words[1] && words[1] < words[2], string.Join(", ", words));
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
        Assert.Equal([new SourceDto(document.Id, "Skript", 1, 2)], summary!.Sources);
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
        var away = Guid.NewGuid();
        service.Material.Down(away);

        using var conflict = await client.PostAsync($"/topics/{empty}/summaries/short", null, Cancel);
        using var unavailable = await client.PostAsync($"/topics/{away}/summaries/short", null, Cancel);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(SummaryEndpoints.NoMaterial, (await conflict.Content.ReadFromJsonAsync<ProblemDto>(Cancel))!.Title);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(SummaryEndpoints.KnowledgeUnavailable, (await unavailable.Content.ReadFromJsonAsync<ProblemDto>(Cancel))!.Title);
    }

    [Fact]
    public async Task PausedAi_TakesNoNewSummary()
    {
        var alice = NewUser();
        var (topic, _, _) = await TopicAsync(alice);
        using var client = ClientFor(alice);
        await SetPausedAsync(true);
        try
        {
            using var response = await client.PostAsync($"/topics/{topic}/summaries/short", null, Cancel);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(SummaryEndpoints.AiPaused, (await response.Content.ReadFromJsonAsync<ProblemDto>(Cancel))!.Title);
            Assert.False(Query(db => db.TopicSummaries.IgnoreQueryFilters().Any(summary => summary.TopicId == topic)));
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

    // A summary that rests on a deleted document goes with it.
    [Fact]
    public async Task DeletedDocument_TakesItsSummaries()
    {
        var alice = NewUser();
        var (topic, document, _) = await TopicAsync(alice);
        using var client = ClientFor(alice);
        await RequestAsync(client, topic, "short", HttpStatusCode.Accepted);
        await WriteAsync();

        using var deleted = await client.DeleteAsync($"/documents/{document}", Cancel);
        using var read = await client.GetAsync($"/topics/{topic}/summaries/short", Cancel);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.False(Query(db => db.TopicSummarySections.IgnoreQueryFilters().Any(section => section.DocumentId == document)));
    }

    // A pasted document with its sections, and Knowledge naming them as one topic's material.
    private async Task<(Guid Topic, Guid Document, IReadOnlyList<Section> Sections)> TopicAsync(string owner)
    {
        var document = Document.Paste(owner, service.Planning.GoalOf(owner), "Photosynthese", Text, DateTime.UtcNow, usesTestDouble: true);
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

    // One round of the worker with the simulated AI.
    private async Task WriteAsync()
    {
        var chat = service.Services.GetRequiredService<TestDoubleAi>().Client;
        await ActivatorUtilities.CreateInstance<SummaryWriter>(service.Services, chat).WriteWaitingAsync(Cancel);
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
        Guid TopicId, string Length, string Status, string? Text, string? FailureCode, bool AiGenerated, string? Model,
        string? PromptVersion, DateTime CreatedAt, DateTime? ReadyAt, SourceDto[] Sources);

    private sealed record SourceDto(Guid DocumentId, string DocumentTitle, int? PageFrom, int? PageTo);

    private sealed record ProblemDto(string Title);
}