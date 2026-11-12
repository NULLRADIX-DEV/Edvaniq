using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #294: a document names where each of its sections stands, in the order of the text and with its pages, only for its
// owner.
public sealed class DocumentSectionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Short paragraphs go together, so the first section runs from page 1 over the page break onto page 2.
    [Fact]
    public async Task FileDocument_GivesEverySectionWithItsPages()
    {
        var alice = NewUser();
        var document = Document.Upload(alice, Guid.NewGuid(), "Skript.pdf", Guid.NewGuid(), FileFormat.Pdf, DateTime.UtcNow);
        var pages = document.TakeText(["# Fotosynthese\nLicht spaltet Wasser.", "Zucker entsteht.", "# Zellatmung\nZucker wird zu Energie."], DateTime.UtcNow);
        var sections = document.Split();
        await StoreAsync(db =>
        {
            db.Documents.Add(document);
            db.DocumentPages.AddRange(pages);
            db.Sections.AddRange(sections);
        });
        using var client = ClientFor(alice);

        var found = await client.GetFromJsonAsync<SectionDto[]>($"/documents/{document.Id}/sections", Cancel);

        Assert.Equal([new SectionDto(sections[0].Id, 0, 1, 2), new SectionDto(sections[1].Id, 1, 3, 3)], found);
        using var foreign = ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/documents/{document.Id}/sections", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/documents/{Guid.NewGuid()}/sections", Cancel)).StatusCode);
    }

    [Fact]
    public async Task PastedText_GivesItsSectionsWithoutPages()
    {
        var alice = NewUser();
        var document = Document.Paste(alice, Guid.NewGuid(), null, TestTexts.Lesson, DateTime.UtcNow);
        var sections = document.Split();
        await StoreAsync(db =>
        {
            db.Documents.Add(document);
            db.Sections.AddRange(sections);
        });
        using var client = ClientFor(alice);

        var found = await client.GetFromJsonAsync<SectionDto[]>($"/documents/{document.Id}/sections", Cancel);

        Assert.Equal(sections.Select(section => new SectionDto(section.Id, section.Ordinal, null, null)), found);
    }

    private async Task StoreAsync(Action<ContentDbContext> add)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        add(db);
        await db.SaveChangesAsync(Cancel);
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private sealed record SectionDto(Guid SectionId, int Position, int? PageFrom, int? PageTo);
}