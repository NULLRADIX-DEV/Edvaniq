using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Api;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #206: document delete <id> lets the operator delete any user's document without their token, the same way the user
// would: everything Content made from it goes, DocumentDeleted goes out once through the outbox.
public sealed class DocumentCommandTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_TakesTheDocumentWithItsSectionsAndFile_AndReportsItOnce()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        using var client = ClientFor(alice);
        using var other = ClientFor(bob);
        var pasted = await PasteAsync(client);
        var kept = await PasteAsync(client);
        var ofBob = await PasteAsync(other);
        var uploaded = await UploadAsync(client);
        var splitter = ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services);
        await splitter.SplitAsync(pasted, Cancel);
        await splitter.SplitAsync(ofBob, Cancel);
        var fileId = Query(db => db.Documents.IgnoreQueryFilters().Single(document => document.Id == uploaded).FileId!.Value);
        await using var command = CommandHost();

        var (exitCode, output, error) = await RunAsync(command, "document", "delete", pasted.ToString());
        var (again, againOutput, _) = await RunAsync(command, "document", "delete", pasted.ToString());
        var (fileExit, _, _) = await RunAsync(command, "document", "delete", uploaded.ToString());

        Assert.Equal((DocumentCommand.Ok, DocumentCommand.Ok, DocumentCommand.Ok), (exitCode, again, fileExit));
        Assert.Empty(error);
        Assert.Equal($"deleted {pasted:D}", output.Trim());
        Assert.Equal($"no document {pasted:D}", againOutput.Trim());
        Assert.DoesNotContain(alice, output, StringComparison.Ordinal);

        Assert.False(Query(db => db.Documents.IgnoreQueryFilters().Any(document => document.Id == pasted || document.Id == uploaded)));
        Assert.Equal(0, Query(db => db.Sections.IgnoreQueryFilters().Count(section => section.DocumentId == pasted)));
        Assert.False(Query(db => db.StoredFiles.IgnoreQueryFilters().Any(file => file.Id == fileId)));
        Assert.False(File.Exists(Path.Combine(service.FilesDirectory, fileId.ToString("N"))));

        var deleted = Deleted();
        var one = Assert.Single(deleted, item => item.DocumentId == pasted);
        Assert.Equal(alice, one.UserId);
        Assert.NotNull(one.LearningGoalId);
        Assert.Single(deleted, item => item.DocumentId == uploaded);
        Assert.DoesNotContain(deleted, item => item.DocumentId == kept || item.DocumentId == ofBob);
        Assert.True(Query(db => db.Documents.IgnoreQueryFilters().Any(document => document.Id == kept)));
        Assert.NotEqual(0, Query(db => db.Sections.IgnoreQueryFilters().Count(section => section.DocumentId == ofBob)));
    }

    [Fact]
    public async Task UnknownId_ChangesNothing()
    {
        await using var command = CommandHost();
        var id = Guid.NewGuid();

        var (exitCode, output, error) = await RunAsync(command, "document", "delete", id.ToString());

        Assert.Equal(DocumentCommand.Ok, exitCode);
        Assert.Empty(error);
        Assert.Equal($"no document {id:D}", output.Trim());
        Assert.DoesNotContain(Deleted(), item => item.DocumentId == id);
    }

    // #1018: only a ready document of some user can be reopened. Anything else is refused by id alone, nothing changes.
    [Fact]
    public async Task Reopen_RefusesUnknownAndUnfinishedDocuments()
    {
        using var client = ClientFor(NewUser("alice"));
        var analyzing = await PasteAsync(client);
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(analyzing, Cancel);
        var unknown = Guid.NewGuid();
        await using var command = CommandHost();

        var (notReady, notReadyOutput, notReadyError) = await RunAsync(command, "document", "reopen", analyzing.ToString());
        var (missing, missingOutput, _) = await RunAsync(command, "document", "reopen", unknown.ToString());

        Assert.Equal((DocumentCommand.Refused, DocumentCommand.Refused), (notReady, missing));
        Assert.Empty(notReadyError);
        Assert.Equal($"not ready {analyzing:D}", notReadyOutput.Trim());
        Assert.Equal($"no document {unknown:D}", missingOutput.Trim());
        var stored = Query(db => db.Documents.IgnoreQueryFilters().Single(document => document.Id == analyzing));
        Assert.Equal((DocumentStatus.Analyzing, (DateTime?)null), (stored.Status, stored.RetriedAt));
    }

    [Theory]
    [InlineData("document")]
    [InlineData("document", "reopen")]
    [InlineData("document", "delete")]
    [InlineData("document", "delete", "not-a-guid")]
    [InlineData("document", "remove", "3f2b9c1e-8d4a-4e0f-9b6a-2c7d1e5f4a10")]
    public async Task OtherArguments_PrintTheUsage(params string[] args)
    {
        await using var command = CommandHost();

        var (exitCode, output, error) = await RunAsync(command, args);

        Assert.Equal(DocumentCommand.Invalid, exitCode);
        Assert.Empty(output);
        Assert.Contains("document delete <id>", error, StringComparison.Ordinal);
    }

    // The running API never gets the operator user or the owner lookup: there the user comes from the token only.
    [Fact]
    public void Api_HasNoOperatorUser()
    {
        using var scope = service.Services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<OperatorUser>());
        Assert.Null(scope.ServiceProvider.GetService<IDocumentOwners>());
        Assert.IsNotType<OperatorUser>(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
    }

    // The service as the command builds it: same database and files, plus what DocumentCommand.Register adds.
    private WebApplicationFactory<Program> CommandHost() =>
        service.WithWebHostBuilder(builder => builder.ConfigureTestServices(DocumentCommand.Register));

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(WebApplicationFactory<Program> host, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await DocumentCommand.RunAsync(host.Services, args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    // Every DocumentDeleted in the outbox, sent or not.
    private List<DocumentDeleted> Deleted()
    {
        var subject = EventRoute.For(typeof(DocumentDeleted)).Subject;
        var payloads = Query(db => db.Set<OutboxMessage>()
            .Where(message => message.Subject == subject)
            .Select(message => message.Payload)
            .ToList());
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<DocumentDeleted>(payload, JsonSerializerOptions.Web)!)];
    }

    private static async Task<Guid> PasteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/documents", new { Title = "Photosynthese", Text = TestTexts.Lesson }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        var file = new ByteArrayContent("Licht spaltet Wasser, dabei entsteht Sauerstoff."u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await client.PostAsync("/documents/files", new MultipartFormDataContent { { file, "file", "Notizen.txt" } }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
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

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";
}