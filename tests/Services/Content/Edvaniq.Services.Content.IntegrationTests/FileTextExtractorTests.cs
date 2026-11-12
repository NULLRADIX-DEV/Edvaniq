using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Files;
using Edvaniq.Services.Content.UnitTests;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// #282: the worker reads the text of an uploaded file page by page, the document keeps text and pages, and the splitter
// takes it from there. The upload itself only stores the file. Each test reads only its own documents, the database is
// shared with other tests.
public sealed class FileTextExtractorTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // AC3: the API never reads a file, only the worker has the extractor.
    [Fact]
    public async Task Upload_StoresTheFileOnly_AndTheApiHasNoExtractor()
    {
        var (pdf, _) = TestPdfs.OneColumn(Paragraphs("biologie-fotosynthese"));

        var id = await UploadAsync(NewUser(), "Fotosynthese.pdf", pdf);

        Assert.Equal(("", (DateTime?)null, DocumentStatus.Uploaded), Query(db => Stored(db, id) is var d ? (d.Text, d.TextExtractedAt, d.Status) : default));
        Assert.Equal(0, Query(db => db.DocumentPages.IgnoreQueryFilters().Count(page => page.DocumentId == id)));
        Assert.DoesNotContain(service.Services.GetServices<IHostedService>(), hosted => hosted is FileTextExtractor);
    }

    // AC1: every page with its number and its text, and each section of the split lies on a page.
    [Fact]
    public async Task Pdf_GetsItsPages_AndIsSplitFromThere()
    {
        var (pdf, written) = TestPdfs.OneColumn(Paragraphs("biologie-fotosynthese"));
        var id = await UploadAsync(NewUser(), "Fotosynthese.pdf", pdf);
        var extractor = Extractor();

        Assert.Contains(id, await extractor.WaitingAsync(Cancel));
        await extractor.ReadAsync(id, Cancel);

        var document = Query(db => Stored(db, id));
        var pages = Query(db => db.DocumentPages.IgnoreQueryFilters().Where(page => page.DocumentId == id).OrderBy(page => page.Number).ToList());
        Assert.Equal(Enumerable.Range(1, written.Count), pages.Select(page => page.Number));
        Assert.Equal(written.Select(TestPdfs.Words), pages.Select(page => TestPdfs.Words(document.Text.Substring(page.Start, page.Length))));
        Assert.NotNull(document.TextExtractedAt);
        Assert.DoesNotContain(id, await extractor.WaitingAsync(Cancel));

        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);

        var sections = Query(db => db.Sections.IgnoreQueryFilters().Where(section => section.DocumentId == id).ToList());
        Assert.Equal(DocumentStatus.Analyzing, Query(db => Stored(db, id).Status));
        Assert.NotEmpty(sections);
        Assert.All(sections, section => Assert.Contains(pages, page => page.Start <= section.Start && section.Start < page.Start + page.Length));
    }

    [Theory]
    [InlineData("Notizen.txt")]
    [InlineData("Notizen.md")]
    public async Task TextFile_IsOnePage(string name)
    {
        const string text = "# Säuren\n\nEine Säure gibt ein Proton ab, eine Base nimmt es auf.";
        var id = await UploadAsync(NewUser(), name, Encoding.UTF8.GetBytes(text));

        await Extractor().ReadAsync(id, Cancel);

        Assert.Equal(text, Query(db => Stored(db, id).Text));
        var page = Assert.Single(Query(db => db.DocumentPages.IgnoreQueryFilters().Where(page => page.DocumentId == id).ToList()));
        Assert.Equal((1, 0, text.Length), (page.Number, page.Start, page.Length));
    }

    public static TheoryData<string, byte[], string> Refused => new()
    {
        { "Lang.pdf", TestPdfs.Empty(Document.MaxPages + 1), Document.TooManyPages },
        { "Kaputt.pdf", "%PDF-1.4\nnichts weiter"u8.ToArray(), Document.FileUnreadable },
    };

    // A file that can't become a document fails it with a reason the user can act on, and leaves no pages.
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task FileThatCantBeRead_FailsTheDocumentWithAReason(string name, byte[] content, string reason)
    {
        var id = await UploadAsync(NewUser(), name, content);

        await Extractor().ReadAsync(id, Cancel);

        Assert.Equal((DocumentStatus.Failed, reason), Query(db => Stored(db, id) is var d ? (d.Status, d.FailureReason) : default));
        Assert.Equal(0, Query(db => db.DocumentPages.IgnoreQueryFilters().Count(page => page.DocumentId == id)));
    }

    [Fact]
    public async Task MissingFile_FailsTheDocument()
    {
        var id = await UploadAsync(NewUser(), "Weg.txt", "Ein Text, dessen Datei verschwindet."u8.ToArray());
        File.Delete(Path.Combine(service.FilesDirectory, Query(db => Stored(db, id).FileId!.Value).ToString("N")));

        await Extractor().ReadAsync(id, Cancel);

        Assert.Equal((DocumentStatus.Failed, Document.FileUnreadable), Query(db => Stored(db, id) is var d ? (d.Status, d.FailureReason) : default));
    }

    // A scan has no text layer: its pages are empty, and the splitter finds nothing to learn from (the hint for scans
    // comes with #284).
    [Fact]
    public async Task Scan_HasEmptyPages_AndNothingToLearn()
    {
        var id = await UploadAsync(NewUser(), "Scan.pdf", TestPdfs.Empty(2));

        await Extractor().ReadAsync(id, Cancel);
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);

        Assert.Equal([0, 0], Query(db => db.DocumentPages.IgnoreQueryFilters().Where(page => page.DocumentId == id).OrderBy(page => page.Number).Select(page => page.Length).ToList()));
        Assert.Equal((DocumentStatus.Failed, Document.NothingToLearn), Query(db => Stored(db, id) is var d ? (d.Status, d.FailureReason) : default));
    }

    // Word files can't be read yet. Instead of waiting forever the document says what to do.
    [Fact]
    public async Task WordFile_FailsWithTheHintToSaveItAsPdf()
    {
        var id = await UploadAsync(NewUser(), "Aufsatz.docx", WordFile());

        await Extractor().ReadAsync(id, Cancel);

        Assert.Equal((DocumentStatus.Failed, Document.WordNotYet), Query(db => Stored(db, id) is var d ? (d.Status, d.FailureReason) : default));
    }

    private static List<string> Paragraphs(string id) =>
        [.. ReferenceSet.Load().Documents.Single(document => document.Id == id).Text.ReplaceLineEndings("\n").Split("\n\n")];

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private static Document Stored(ContentDbContext db, Guid id) => db.Documents.IgnoreQueryFilters().Single(document => document.Id == id);

    private static byte[] WordFile()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            entry.Write("<w:document/>");
        }

        return stream.ToArray();
    }

    private FileTextExtractor Extractor() => ActivatorUtilities.CreateInstance<FileTextExtractor>(service.Services);

    private async Task<Guid> UploadAsync(string user, string name, byte[] content)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { file, "file", name } };
        using var response = await client.PostAsync("/documents/files", form, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }
}