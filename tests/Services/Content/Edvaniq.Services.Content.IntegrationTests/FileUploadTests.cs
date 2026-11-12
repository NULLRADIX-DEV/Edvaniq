using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #275: PDF, DOCX, TXT and Markdown come in as documents in status Uploaded, judged by their bytes. Everything else, too
// large or into someone else's goal is turned away, and nothing of it stays behind.
public sealed class FileUploadTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static TheoryData<string, FileFormat> Accepted => new()
    {
        { "Skript.pdf", FileFormat.Pdf },
        { "Aufsatz.docx", FileFormat.Docx },
        { "Notizen.txt", FileFormat.Text },
        { "Zusammenfassung.md", FileFormat.Markdown },
    };

    // AC1 and AC5.
    [Theory]
    [MemberData(nameof(Accepted))]
    public async Task AllowedFile_BecomesAnUploadedDocument(string name, FileFormat format)
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, name, FileOf(format));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("Uploaded", body.GetProperty("status").GetString());
        Assert.Equal("File", body.GetProperty("source").GetString());
        Assert.Equal(Path.GetFileNameWithoutExtension(name), body.GetProperty("title").GetString());

        var stored = Assert.Single(await DocumentsOfAsync(alice));
        Assert.Equal((format, service.Planning.GoalOf(alice)), (stored.FileFormat, stored.LearningGoalId));
        Assert.True(File.Exists(PathOf(stored.FileId!.Value)));

        using var list = await client.GetAsync("/documents", Cancel);
        var listed = Assert.Single((await list.Content.ReadFromJsonAsync<JsonElement>(Cancel)).EnumerateArray());
        Assert.Equal(("Uploaded", "File"), (listed.GetProperty("status").GetString(), listed.GetProperty("source").GetString()));
    }

    // #202: deleting a document from a file takes the file along, bytes and row. Someone else's file stays.
    [Fact]
    public async Task DeletedFileDocument_TakesItsFile_OthersStay()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        using var client = ClientFor(alice);
        using var other = ClientFor(bob);
        (await UploadAsync(client, "Notizen.txt", FileOf(FileFormat.Text))).Dispose();
        (await UploadAsync(other, "Notizen.txt", FileOf(FileFormat.Text))).Dispose();
        var document = Assert.Single(await DocumentsOfAsync(alice));
        var kept = Assert.Single(await DocumentsOfAsync(bob));

        using var deleted = await client.DeleteAsync($"/documents/{document.Id}", Cancel);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(File.Exists(PathOf(document.FileId!.Value)));
        await AssertNothingStoredAsync(alice);
        Assert.True(File.Exists(PathOf(kept.FileId!.Value)));
        Assert.Equal(1, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(file => file.OwnerId == bob)));
    }

    // Until the worker has read its text, the splitter leaves a document from a file alone.
    [Fact]
    public async Task Splitter_LeavesTheFileDocumentAlone()
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);
        using var response = await UploadAsync(client, "Notizen.txt", FileOf(FileFormat.Text));
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
        var splitter = ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services);

        Assert.DoesNotContain(id, await splitter.WaitingAsync(Cancel));
        await splitter.SplitAsync(id, Cancel);

        Assert.Equal(DocumentStatus.Uploaded, Assert.Single(await DocumentsOfAsync(alice)).Status);
        Assert.Equal(0, Query(db => db.Sections.IgnoreQueryFilters().Count(section => section.DocumentId == id)));
    }

    public static TheoryData<string, byte[]> WrongContent => new()
    {
        { "Bild.pdf", Png() },
        { "Folien.docx", Zip("ppt/presentation.xml") },
        { "Skript.docx", FileOf(FileFormat.Pdf) },
        { "Binär.txt", [.. "Text"u8.ToArray(), 0x00, 0xFF, 0xFE] },
        { "Bild.md", Png() },
    };

    // AC1: an allowed extension with other content.
    [Theory]
    [MemberData(nameof(WrongContent))]
    public async Task AllowedExtensionWithOtherContent_IsRefused_AndNothingStays(string name, byte[] content)
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, name, content);

        await AssertRefusedAsync(response, FileCheck.Mismatch);
        await AssertNothingStoredAsync(alice);
    }

    // AC2: whatever the content, a type outside the list never gets stored.
    [Theory]
    [InlineData("Folien.pptx")]
    [InlineData("Foto.png")]
    [InlineData("Tabelle.xlsx")]
    [InlineData("ohne-endung")]
    public async Task OtherType_IsRefused(string name)
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, name, Zip("ppt/presentation.xml"));

        await AssertRefusedAsync(response, FileCheck.Unsupported);
        await AssertNothingStoredAsync(alice);
    }

    [Fact]
    public async Task EmptyFile_IsRefused()
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, "leer.txt", []);

        await AssertRefusedAsync(response, FileCheck.Empty);
        await AssertNothingStoredAsync(alice);
    }

    // The browser's content type counts for nothing, the bytes decide.
    [Fact]
    public async Task ContentTypeOfTheBrowser_IsIgnored()
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, "Bild.pdf", Png(), "application/pdf");

        await AssertRefusedAsync(response, FileCheck.Mismatch);
    }

    // A file of exactly 20 MB still comes in.
    [Fact]
    public async Task FileOfExactly20MB_IsAccepted()
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);
        var content = new byte[FileCheck.MaxLength];
        Array.Fill(content, (byte)'a');

        using var response = await UploadAsync(client, "gross.txt", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // AC3, with and without Content-Length.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FileOver20MB_IsRefusedWithTheLimit_AndNothingStays(bool withLength)
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);
        var content = new byte[FileCheck.MaxLength + 1];
        Array.Fill(content, (byte)'a');

        using var form = Form("gross.txt", content);
        HttpContent body = withLength ? form : new ChunkedContent(form);
        using var response = await client.PostAsync("/documents/files?confirmation=material-1", body, Cancel);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FileCheck.TooLarge, (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("title").GetString());
        await AssertNothingStoredAsync(alice);
    }

    // AC4: someone else's goal looks like none, and nothing is stored.
    [Fact]
    public async Task UploadIntoSomeoneElsesGoal_IsRefused()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var bobsGoal = service.Planning.GoalOf(bob);
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, "Notizen.txt", FileOf(FileFormat.Text), goal: bobsGoal);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Dieses Lernziel gibt es nicht. Wähle eines deiner Lernziele aus.",
            (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("title").GetString());
        await AssertNothingStoredAsync(alice);
        Assert.Empty(await DocumentsOfAsync(bob));
    }

    [Fact]
    public async Task UploadIntoOwnGoal_LandsThere()
    {
        var alice = NewUser("alice");
        var goal = service.Planning.GoalOf(alice);
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, "Notizen.txt", FileOf(FileFormat.Text), goal: goal);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(goal, Assert.Single(await DocumentsOfAsync(alice)).LearningGoalId);
    }

    [Fact]
    public async Task PlanningDown_IsRefusedWithAHint_AndNothingStays()
    {
        var alice = NewUser("alice");
        service.Planning.Down(alice);
        using var client = ClientFor(alice);

        using var response = await UploadAsync(client, "Notizen.txt", FileOf(FileFormat.Text));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNothingStoredAsync(alice);
    }

    // A client that breaks off mid-upload leaves neither a part file nor a row.
    [Fact]
    public async Task UploadBrokenOff_LeavesNothing()
    {
        var alice = NewUser("alice");
        using var client = ClientFor(alice);
        var before = Directory.Exists(service.FilesDirectory) ? Directory.GetFiles(service.FilesDirectory).ToHashSet() : [];

        using var form = new MultipartFormDataContent();
        var file = new StreamContent(new BreakingStream(1024 * 1024));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "abgebrochen.txt");
        await Assert.ThrowsAnyAsync<Exception>(() => client.PostAsync("/documents/files?confirmation=material-1", form, Cancel));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.GetFiles(service.FilesDirectory).Except(before).Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, Cancel);
        }

        Assert.Empty(Directory.GetFiles(service.FilesDirectory).Except(before));
        await AssertNothingStoredAsync(alice);
    }

    [Fact]
    public async Task WithoutAFile_IsRefused()
    {
        using var client = ClientFor(NewUser("alice"));

        using var response = await client.PostAsync("/documents/files?confirmation=material-1", new MultipartFormDataContent { { new StringContent("x"), "note" } }, Cancel);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("errors").GetProperty("file");
        Assert.Equal(message, Assert.Single(errors.EnumerateArray()).GetString());
    }

    private async Task AssertNothingStoredAsync(string userId)
    {
        Assert.Empty(await DocumentsOfAsync(userId));
        Assert.Equal(0, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(file => file.OwnerId == userId)));
    }

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string name, byte[] content, string contentType = "application/octet-stream", Guid? goal = null) =>
        client.PostAsync(
            goal is { } id ? $"/documents/files?learningGoalId={id}&confirmation=material-1" : "/documents/files?confirmation=material-1", Form(name, content, contentType), Cancel);

    private static MultipartFormDataContent Form(string name, byte[] content, string contentType = "application/octet-stream")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    private static byte[] FileOf(FileFormat format) => format switch
    {
        FileFormat.Pdf => "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray(),
        FileFormat.Docx => Zip("word/document.xml"),
        FileFormat.Text => "Licht spaltet Wasser, dabei entsteht Sauerstoff."u8.ToArray(),
        _ => "# Photosynthese\n\n- Lichtreaktion\n- Calvin-Zyklus\n"u8.ToArray(),
    };

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    private static byte[] Zip(string entry)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write("<?xml version=\"1.0\"?><document/>");
        }

        return stream.ToArray();
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private string PathOf(Guid id) => Path.Combine(service.FilesDirectory, id.ToString("N"));

    private async Task<List<Document>> DocumentsOfAsync(string userId)
    {
        using var scope = service.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters()
            .Where(document => document.OwnerId == userId)
            .ToListAsync(Cancel);
    }

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    // The same body without Content-Length, so only the count while reading can stop it. The content type with the
    // boundary comes along, otherwise there's no multipart to read.
    private sealed class ChunkedContent : HttpContent
    {
        private readonly HttpContent inner;

        public ChunkedContent(HttpContent inner)
        {
            this.inner = inner;
            Headers.ContentType = inner.Headers.ContentType;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            inner.CopyTo(stream, context, cancellationToken);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => inner.CopyToAsync(stream, context);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            inner.CopyToAsync(stream, context, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    // Gives some bytes, then fails like a connection that drops.
    private sealed class BreakingStream(long before) : Stream
    {
        private long given;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => given;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (given >= before)
            {
                throw new IOException("Connection dropped.");
            }

            var n = (int)Math.Min(count, before - given);
            Array.Fill(buffer, (byte)'a', offset, n);
            given += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}