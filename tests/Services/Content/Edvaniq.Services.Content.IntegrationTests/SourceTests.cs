using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Api;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #350: a source opens in the original. A section of a PDF names its pages, any other section comes with the text
// around it. The uploaded file goes back to its owner only, with headers that keep it from running anything.
public sealed class SourceTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // AC1: the page of a PDF, no text.
    [Fact]
    public async Task SectionOfAPdf_NamesItsPages()
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

        var source = await client.GetFromJsonAsync<JsonElement>($"/sections/{sections[1].Id}/source", Cancel);

        Assert.Equal(document.Id, source.GetProperty("documentId").GetGuid());
        Assert.Equal(sections[1].Id, source.GetProperty("sectionId").GetGuid());
        Assert.Equal(1, source.GetProperty("position").GetInt32());
        Assert.Equal(("Skript", "pdf", true), (source.GetProperty("title").GetString(), source.GetProperty("format").GetString(), source.GetProperty("hasFile").GetBoolean()));
        Assert.Equal((3, 3), (source.GetProperty("page").GetInt32(), source.GetProperty("pageTo").GetInt32()));
        Assert.Equal(JsonValueKind.Null, source.GetProperty("text").ValueKind);
    }

    // AC2: a text file and a pasted text come with the window around the section, start and length inside it.
    [Fact]
    public async Task SectionOfATextFile_ComesWithItsWindow()
    {
        var alice = NewUser();
        var document = Document.Upload(alice, Guid.NewGuid(), "Notizen.txt", Guid.NewGuid(), FileFormat.Text, DateTime.UtcNow);
        var pages = document.TakeText([Parts(4)], DateTime.UtcNow);
        var sections = document.Split();
        await StoreAsync(db =>
        {
            db.Documents.Add(document);
            db.DocumentPages.AddRange(pages);
            db.Sections.AddRange(sections);
        });
        using var client = ClientFor(alice);

        var middle = await client.GetFromJsonAsync<JsonElement>($"/sections/{sections[1].Id}/source", Cancel);
        var last = await client.GetFromJsonAsync<JsonElement>($"/sections/{sections[3].Id}/source", Cancel);

        Assert.Equal(("text", true), (middle.GetProperty("format").GetString(), middle.GetProperty("hasFile").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, middle.GetProperty("page").ValueKind);
        AssertWindow(middle, document.Text, sections[1], from: sections[0], to: sections[2], before: false, after: true);
        AssertWindow(last, document.Text, sections[3], from: sections[2], to: sections[3], before: true, after: false);
    }

    [Fact]
    public async Task SectionOfAPastedText_ComesWithItsWindow()
    {
        var alice = NewUser();
        var document = Document.Paste(alice, Guid.NewGuid(), "Biologie", Parts(3), DateTime.UtcNow);
        var sections = document.Split();
        await StoreAsync(db =>
        {
            db.Documents.Add(document);
            db.Sections.AddRange(sections);
        });
        using var client = ClientFor(alice);

        var first = await client.GetFromJsonAsync<JsonElement>($"/sections/{sections[0].Id}/source", Cancel);

        Assert.Equal(JsonValueKind.Null, first.GetProperty("format").ValueKind);
        Assert.False(first.GetProperty("hasFile").GetBoolean());
        AssertWindow(first, document.Text, sections[0], from: sections[0], to: sections[1], before: false, after: true);
    }

    // AC3: someone else's section, one of a deleted document and an unknown id all look the same.
    [Fact]
    public async Task ForeignDeletedOrUnknownSection_IsNotFound()
    {
        var alice = NewUser();
        var document = Document.Paste(alice, Guid.NewGuid(), "Biologie", Parts(2), DateTime.UtcNow);
        var sections = document.Split();
        await StoreAsync(db =>
        {
            db.Documents.Add(document);
            db.Sections.AddRange(sections);
        });
        using var owner = ClientFor(alice);
        using var bob = ClientFor(NewUser());

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(bob, $"/sections/{sections[0].Id}/source"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(owner, $"/sections/{Guid.NewGuid()}/source"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(owner, $"/sections/{sections[0].Id}/source"));

        using var deleted = await owner.DeleteAsync($"/documents/{document.Id}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(owner, $"/sections/{sections[0].Id}/source"));
    }

    [Fact]
    public async Task OwnPdf_ComesBackInline_WithHeadersThatRunNothing()
    {
        using var client = ClientFor(NewUser());
        var id = await UploadAsync(client, "Äpfel und Birnen.pdf", Pdf);

        using var response = await client.GetAsync($"/documents/{id}/file", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Pdf, await response.Content.ReadAsStringAsync(Cancel));
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("inline", disposition.DispositionType);
        Assert.Equal("Äpfel und Birnen.pdf", disposition.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(SourceEndpoints.FilePolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    // The PDF viewer loads a large file in pieces.
    [Fact]
    public async Task OwnFile_ComesInRanges()
    {
        using var client = ClientFor(NewUser());
        var id = await UploadAsync(client, "Skript.pdf", Pdf);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/documents/{id}/file");
        request.Headers.Range = new RangeHeaderValue(0, 3);

        using var response = await client.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("%PDF", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Theory]
    [InlineData("Notizen.txt", "text/plain", "inline")]
    [InlineData("Notizen.md", "text/plain", "inline")]
    public async Task OwnTextFile_ComesAsPlainText(string name, string type, string dispositionType)
    {
        using var client = ClientFor(NewUser());
        var id = await UploadAsync(client, name, "# <script>alert(1)</script>\nLicht spaltet Wasser.");

        using var response = await client.GetAsync($"/documents/{id}/file", Cancel);

        Assert.Equal(type, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal(dispositionType, response.Content.Headers.ContentDisposition?.DispositionType);
    }

    // AC3 for the file, a range included: the owner check comes before the bytes.
    [Fact]
    public async Task ForeignFile_IsNotFound_AlsoAsARange()
    {
        using var alice = ClientFor(NewUser());
        using var bob = ClientFor(NewUser());
        var id = await UploadAsync(alice, "Skript.pdf", Pdf);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/documents/{id}/file");
        request.Headers.Range = new RangeHeaderValue(0, 3);

        using var ranged = await bob.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, ranged.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(bob, $"/documents/{id}/file"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(alice, $"/documents/{id}/file"));
    }

    [Fact]
    public async Task PastedText_HasNoFile()
    {
        using var client = ClientFor(NewUser());
        using var pasted = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson, confirmation = "material-1" }, Cancel);
        var id = (await pasted.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, $"/documents/{id}/file"));
    }

    private static void AssertWindow(JsonElement source, string text, Section section, Section from, Section to, bool before, bool after)
    {
        Assert.Equal(section.Ordinal, source.GetProperty("position").GetInt32());
        var window = source.GetProperty("text").GetString()!;
        var start = source.GetProperty("start").GetInt32();
        var length = source.GetProperty("length").GetInt32();
        Assert.Equal(text[from.Start..(to.Start + to.Length)], window);
        Assert.Equal(text.Substring(section.Start, section.Length), window.Substring(start, length));
        Assert.Equal((before, after), (source.GetProperty("truncatedBefore").GetBoolean(), source.GetProperty("truncatedAfter").GetBoolean()));
    }

    // Parts with a heading each, so every one becomes a section of its own.
    private static string Parts(int count) => string.Join(
        "\n\n",
        Enumerable.Range(1, count).Select(number => $"# Kapitel {number}\nLicht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff, Teil {number}."));

    private async Task<Guid> UploadAsync(HttpClient client, string name, string content)
    {
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { file, "file", name } };
        using var response = await client.PostAsync("/documents/files?confirmation=material-1", form, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Cancel);
        return response.StatusCode;
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
}

// #350: the title in the header of the file, without a database.
public sealed class FileDispositionTests
{
    // The title is the user's: quotes, a line break and umlauts never break the header or end up raw in it.
    [Theory]
    [InlineData("Äpfel \"und\" Birnen", FileFormat.Pdf, "inline", "Äpfel \"und\" Birnen.pdf")]
    [InlineData("Zeile\r\nX-Injected: 1", FileFormat.Text, "inline", "Zeile\r\nX-Injected: 1.txt")]
    [InlineData("Aufsatz; filename=evil.exe", FileFormat.Docx, "attachment", "Aufsatz; filename=evil.exe.docx")]
    public void Disposition_CarriesTheTitleSafely(string title, FileFormat format, string type, string name)
    {
        var header = SourceEndpoints.DispositionOf(title, format);

        Assert.DoesNotContain('\r', header);
        Assert.DoesNotContain('\n', header);
        Assert.All(header, c => Assert.InRange(c, ' ', '~'));
        var parsed = ContentDispositionHeaderValue.Parse(header);
        Assert.Equal(type, parsed.DispositionType);
        Assert.Equal(name, parsed.FileNameStar);
        Assert.Equal(2, parsed.Parameters.Count);
    }
}