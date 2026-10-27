using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// The worker splits a pasted text into sections after the API has answered. Each section points into its document, a
// second run adds none twice, and a text without content fails with a reason. The tests share the database with other
// tests, so they split only their own documents and never run a whole round.
public sealed class SectionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Text = "# Photosynthese\nLicht spaltet Wasser.\n\n# Zellatmung\nZucker wird zu Energie.";

    private DocumentSplitter Splitter => ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services);

    [Fact]
    public async Task PastedText_IsSplitInTheBackground_AndTheDocumentGoesIntoAnalysis()
    {
        using var client = ClientFor($"alice-{Guid.NewGuid():N}");

        var created = await PasteAsync(client, Text);

        Assert.Equal("Uploaded", created.Status);
        Assert.Contains(created.Id, await Splitter.WaitingAsync(TestContext.Current.CancellationToken));
        await Splitter.SplitAsync(created.Id, TestContext.Current.CancellationToken);
        var found = await client.GetFromJsonAsync<DocumentDto>($"/documents/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(("Analyzing", null), (found!.Status, found.FailureReason));
        Assert.Equal(["# Photosynthese\nLicht spaltet Wasser.", "# Zellatmung\nZucker wird zu Energie."], await SectionTextsAsync(created.Id));
    }

    [Fact]
    public async Task SplittingAgain_AddsNoSectionTwice()
    {
        using var client = ClientFor($"alice-{Guid.NewGuid():N}");
        var created = await PasteAsync(client, Text);
        await Splitter.SplitAsync(created.Id, TestContext.Current.CancellationToken);

        // Once more while it is in analysis, then once more after it came back to Uploaded.
        await Splitter.SplitAsync(created.Id, TestContext.Current.CancellationToken);
        using (var scope = service.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters()
                .Where(document => document.Id == created.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(document => document.Status, DocumentStatus.Uploaded), TestContext.Current.CancellationToken);
        }

        await Splitter.SplitAsync(created.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, (await SectionTextsAsync(created.Id)).Count);
    }

    [Fact]
    public async Task TextWithoutContent_FailsWithAReason()
    {
        // The API turns such a text away, so it goes straight into the database.
        var alice = $"alice-{Guid.NewGuid():N}";
        var document = Document.Paste(alice, "Leer", " \r\n\t ", DateTime.UtcNow);
        using (var scope = service.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
            db.Documents.Add(document);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await Splitter.SplitAsync(document.Id, TestContext.Current.CancellationToken);

        using var client = ClientFor(alice);
        var found = await client.GetFromJsonAsync<DocumentDto>($"/documents/{document.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(
            ("Failed", "Der Text enthält nichts, woraus sich lernen lässt. Füge einen anderen Text ein."),
            (found!.Status, found.FailureReason));
        Assert.Empty(await SectionTextsAsync(document.Id));
    }

    private async Task<DocumentDto> PasteAsync(HttpClient client, string text)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(TestContext.Current.CancellationToken))!;
    }

    // The texts the sections of a document point to, in their order.
    private async Task<List<string>> SectionTextsAsync(Guid documentId)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var text = await db.Documents.IgnoreQueryFilters().Where(document => document.Id == documentId)
            .Select(document => document.Text).SingleAsync(TestContext.Current.CancellationToken);
        var sections = await db.Sections.IgnoreQueryFilters().Where(section => section.DocumentId == documentId)
            .OrderBy(section => section.Ordinal).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(0, sections.Count), sections.Select(section => section.Ordinal));
        return [.. sections.Select(section => text.Substring(section.Start, section.Length))];
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private sealed record DocumentDto(Guid Id, string Title, string Status, string? FailureReason, DateTime CreatedAt);
}
