using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Content.Infrastructure.Files;
using Edvaniq.Services.Content.UnitTests;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edvaniq.Services.Content.IntegrationTests;

// #284, A12: the scan of the reference set as the PDF a scanner makes, pictures and nothing but page numbers and stray
// marks as text. Uploaded, it fails with the hint for scans, and the whole worker runs over it without asking the AI:
// no attempt in the cost log, no topics reported.
public sealed class ScanTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ScanOfTheReferenceSet_FailsWithTheHint_WithoutAskingTheAi()
    {
        var scan = ReferenceSet.Load().Documents.Single(document => document.Kind == "scan");
        var id = await UploadAsync(TestPdfs.Scan(scan.Text));
        var ai = new CountingChatClient();

        await Extractor().ReadAsync(id, Cancel);
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, ai, NullLogger<DocumentAnalyzer>.Instance).AnalyzeAsync(id, Cancel);

        Assert.Equal((DocumentStatus.Failed, Document.NoTextInFile), Query(db => Stored(db, id) is var d ? (d.Status, d.FailureReason) : default));
        Assert.Equal(0, Query(db => db.DocumentPages.IgnoreQueryFilters().Count(page => page.DocumentId == id)));
        Assert.Equal(0, Query(db => db.Sections.IgnoreQueryFilters().Count(section => section.DocumentId == id)));
        Assert.Equal(0, Query(db => db.AiAttempts.IgnoreQueryFilters().Count(attempt => attempt.DocumentId == id)));
        Assert.Equal(0, ai.Calls);
        Assert.DoesNotContain(Query(db => db.Set<OutboxMessage>().Select(message => message.Payload).ToList()), payload => payload.Contains(id.ToString(), StringComparison.Ordinal));
    }

    // The user sees the hint as the reason of the document, as for every other failure.
    [Fact]
    public async Task ScanHint_IsTheReasonTheUserSees()
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var id = await UploadAsync(TestPdfs.Scan(ReferenceSet.Load().Documents.Single(document => document.Kind == "scan").Text), user);

        await Extractor().ReadAsync(id, Cancel);

        using var client = ClientFor(user);
        var document = await client.GetFromJsonAsync<JsonElement>($"/documents/{id}", Cancel);
        Assert.Equal(("Failed", Document.NoTextInFile), (document.GetProperty("status").GetString(), document.GetProperty("failureReason").GetString()));
    }

    // The edge case: few words on the pages with text, the rest pictures. Under the rule for pasted text the file is a
    // scan, from eight words on its text pages go on.
    [Theory]
    [InlineData("Seite eins zeigt Pflanzen", DocumentStatus.Failed)]
    [InlineData("Seite eins zeigt Pflanzen, Licht, Wasser, Zucker und Sauerstoff im Blatt", DocumentStatus.Uploaded)]
    public async Task FewWordsBesidePictures_CountForTheWholeFile(string words, DocumentStatus status)
    {
        var id = await UploadAsync(TestPdfs.Scan($"- 1 -\n{words}\n- 2 -\n"));

        await Extractor().ReadAsync(id, Cancel);

        Assert.Equal(status, Query(db => Stored(db, id).Status));
    }

    private FileTextExtractor Extractor() => ActivatorUtilities.CreateInstance<FileTextExtractor>(service.Services);

    private async Task<Guid> UploadAsync(byte[] pdf, string? user = null)
    {
        using var client = ClientFor(user ?? $"alice-{Guid.NewGuid():N}");
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { file, "file", "Scan Erdkunde.pdf" } };
        using var response = await client.PostAsync("/documents/files", form, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private HttpClient ClientFor(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private static Document Stored(ContentDbContext db, Guid id) => db.Documents.IgnoreQueryFilters().Single(document => document.Id == id);

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    // Counts every call and answers none: a scan must never get here.
    private sealed class CountingChatClient : IChatClient
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("The AI was asked about a scan.");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("The AI was asked about a scan.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}