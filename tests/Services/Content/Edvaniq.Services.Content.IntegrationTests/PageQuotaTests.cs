using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Files;
using Edvaniq.SharedKernel;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #301: every user may have a few pages processed per study day (Content:PagesPerDay, 3 here). A document that doesn't
// fit is turned away with the time the quota starts again, early in the Api where its pages are known, else in the
// worker before the AI. A document takes its pages once, a retry nothing, a deletion gives nothing back, and at 04:00 in
// Berlin it all starts again. Each test has its own users.
public sealed class PageQuotaTests(PageQuotaFactory service) : IClassFixture<PageQuotaFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PastedTextOverTheRest_IsTurnedAwayInTheApi_WithTheReset()
    {
        var alice = NewUser();
        using var client = ClientFor(alice);
        var first = await PasteAsync(client, Text(pages: 2));
        await SplitAsync(first);

        using var response = await client.PostAsJsonAsync("/documents", new { text = Text(pages: 2) }, Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var now = service.Time.GetUtcNow().UtcDateTime;
        Assert.Equal(PageQuota.TooManyPages(2, 1, now), problem.GetProperty("title").GetString());
        Assert.Equal(StudyDay.NextStartUtc(now), problem.GetProperty("resetsAt").GetDateTime().ToUniversalTime());
        Assert.Equal(1, Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == alice)));
        Assert.Equal((1, 3), await QuotaAsync(client));
    }

    // A file's pages are only known once the worker read it: it fails there, before the AI, and works after 04:00.
    [Fact]
    public async Task FileOverTheRest_FailsInTheWorker_AndWorksAfterTheReset()
    {
        var alice = NewUser();
        using var client = ClientFor(alice);
        await SplitAsync(await PasteAsync(client, Text(pages: 2)));
        var file = await UploadAsync(client, Text(pages: 3));

        await ActivatorUtilities.CreateInstance<FileTextExtractor>(service.Services).ReadAsync(file, Cancel);
        await SplitAsync(file);

        var now = service.Time.GetUtcNow().UtcDateTime;
        var failed = Stored(file);
        Assert.Equal((DocumentStatus.Failed, PageQuota.TooManyPages(3, 1, now)), (failed.Status, failed.FailureReason));
        Assert.Equal(0, Query(db => db.Sections.IgnoreQueryFilters().Count(section => section.DocumentId == file)));
        Assert.False(Query(db => db.PageCharges.IgnoreQueryFilters().Any(charge => charge.DocumentId == file)));

        service.Time.Advance(StudyDay.NextStartUtc(now) - now);
        Assert.Equal((3, 3), await QuotaAsync(client));
        using var retry = await client.PostAsync($"/documents/{file}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        await SplitAsync(file);

        Assert.Equal(DocumentStatus.Analyzing, Stored(file).Status);
        Assert.Equal((0, 3), await QuotaAsync(client));
    }

    // A retry takes nothing twice, and deleting gives nothing back, or deleting and pasting again would reset the quota.
    [Fact]
    public async Task Retry_TakesNothingTwice_AndDeletionGivesNothingBack()
    {
        var alice = NewUser();
        using var client = ClientFor(alice);
        var id = await PasteAsync(client, Text(pages: 2));
        await SplitAsync(id);
        Query(db => db.Documents.IgnoreQueryFilters().Where(document => document.Id == id)
            .ExecuteUpdate(set => set
                .SetProperty(document => document.Status, DocumentStatus.Failed)
                .SetProperty(document => document.FailureReason, Document.AiUnavailable)));

        using var retry = await client.PostAsync($"/documents/{id}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        await SplitAsync(id);

        Assert.Equal(1, Query(db => db.PageCharges.IgnoreQueryFilters().Count(charge => charge.DocumentId == id)));
        Assert.Equal((1, 3), await QuotaAsync(client));

        using var deleted = await client.DeleteAsync($"/documents/{id}", Cancel);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal((1, 3), await QuotaAsync(client));
    }

    // With nothing left an upload is turned away at once, before a byte is stored.
    [Fact]
    public async Task Upload_WithNothingLeft_IsTurnedAwayInTheApi()
    {
        var alice = NewUser();
        using var client = ClientFor(alice);
        await SplitAsync(await PasteAsync(client, Text(pages: 3)));

        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(Text(pages: 1)));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { file, "file", "Notizen.txt" } };
        using var response = await client.PostAsync("/documents/files", form, Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(PageQuota.UsedUp(service.Time.GetUtcNow().UtcDateTime), problem.GetProperty("title").GetString());
        Assert.False(Query(db => db.StoredFiles.IgnoreQueryFilters().Any(stored => stored.OwnerId == alice)));
    }

    // GET /quota shows the caller's own count only, the generations follow with #302.
    [Fact]
    public async Task Quota_ShowsTheCallersOwnCount()
    {
        using var alice = ClientFor(NewUser());
        using var bob = ClientFor(NewUser());
        await SplitAsync(await PasteAsync(alice, Text(pages: 2)));

        var quota = await alice.GetFromJsonAsync<JsonElement>("/quota", Cancel);

        Assert.Equal(
            ["pagesLeftToday", "pagesPerDay", "generationsLeftToday", "generationsPerDay", "resetsAt"],
            quota.EnumerateObject().Select(field => field.Name));
        Assert.Equal(JsonValueKind.Null, quota.GetProperty("generationsLeftToday").ValueKind);
        Assert.Equal(
            StudyDay.NextStartUtc(service.Time.GetUtcNow().UtcDateTime),
            quota.GetProperty("resetsAt").GetDateTime().ToUniversalTime());
        Assert.Equal((1, 3), await QuotaAsync(alice));
        Assert.Equal((3, 3), await QuotaAsync(bob));
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    // A text of so many pages: whole sentences up to the last character of the page count.
    private static string Text(int pages)
    {
        const string sentence = "Licht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff. ";
        var text = new StringBuilder();
        while (text.Length + sentence.Length <= pages * PageQuota.CharactersPerPage)
        {
            text.Append(sentence);
        }

        return text.ToString().TrimEnd();
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Guid> PasteAsync(HttpClient client, string text)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string text)
    {
        using var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { file, "file", "Notizen.txt" } };
        using var response = await client.PostAsync("/documents/files", form, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<(int Left, int PerDay)> QuotaAsync(HttpClient client)
    {
        var quota = await client.GetFromJsonAsync<JsonElement>("/quota", Cancel);
        return (quota.GetProperty("pagesLeftToday").GetInt32(), quota.GetProperty("pagesPerDay").GetInt32());
    }

    private Task SplitAsync(Guid id) => ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);

    private Document Stored(Guid id) => Query(db => db.Documents.IgnoreQueryFilters().AsNoTracking().Single(document => document.Id == id));

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }
}

// The service with a quota of 3 pages and a clock the tests move.
public sealed class PageQuotaFactory(MySqlTestServer mysql) : ServiceFactory(mysql)
{
    public TestTime Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Content:PagesPerDay", "3")
            .ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }
}