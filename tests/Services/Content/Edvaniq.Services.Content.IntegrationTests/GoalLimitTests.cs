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

// #277: a learning goal holds at most ten documents. The eleventh is refused with 409 and a message that names the
// limit and what to do, pasted or uploaded, before anything is stored. Every document counts until it is deleted, a
// failed one too, one of another goal doesn't.
public sealed class GoalLimitTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Text = "Licht spaltet Wasser in der Lichtreaktion, und im Calvin-Zyklus entsteht daraus Zucker.";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TenthDocumentFits_TheEleventhIsRefused()
    {
        var user = NewUser();
        using var client = ClientFor(user);
        for (var document = 0; document < Document.MaxDocumentsPerGoal; document++)
        {
            using var pasted = await PasteAsync(client);
            Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
        }

        using var eleventh = await PasteAsync(client);

        await AssertGoalFullAsync(eleventh);
        Assert.Equal(Document.MaxDocumentsPerGoal, Count(user));
    }

    // Refused before the body is read: no file of the eleventh document is stored.
    [Fact]
    public async Task EleventhUpload_IsRefused_AndNothingIsStored()
    {
        var user = NewUser();
        await SeedAsync(user, service.Planning.GoalOf(user), Document.MaxDocumentsPerGoal);
        using var client = ClientFor(user);
        var file = new ByteArrayContent("Ein Text über die Photosynthese in grünen Pflanzen und Algen."u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var form = new MultipartFormDataContent { { file, "file", "Notizen.txt" } };

        using var response = await client.PostAsync("/documents/files?confirmation=material-1", form, Cancel);

        await AssertGoalFullAsync(response);
        Assert.Equal(0, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(stored => stored.OwnerId == user)));
    }

    // A failed document keeps its place, it can still be started again. Deleting one makes room.
    [Fact]
    public async Task FailedDocumentsCount_DeletingOneMakesRoom()
    {
        var user = NewUser();
        var ids = await SeedAsync(user, service.Planning.GoalOf(user), Document.MaxDocumentsPerGoal, failed: true);
        using var client = ClientFor(user);
        using (var full = await PasteAsync(client))
        {
            await AssertGoalFullAsync(full);
        }

        using (var deleted = await client.DeleteAsync($"/documents/{ids[0]}", Cancel))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using var pasted = await PasteAsync(client);
        Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
    }

    [Fact]
    public async Task DocumentsOfAnotherGoal_DontCount()
    {
        var user = NewUser();
        await SeedAsync(user, Guid.NewGuid(), Document.MaxDocumentsPerGoal);
        using var client = ClientFor(user);

        using var pasted = await PasteAsync(client);

        Assert.Equal(HttpStatusCode.Created, pasted.StatusCode);
    }

    private static async Task AssertGoalFullAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(Document.GoalFull, problem.GetProperty("title").GetString());
        Assert.Contains("10", Document.GoalFull, StringComparison.Ordinal);
    }

    private async Task<List<Guid>> SeedAsync(string user, Guid goal, int count, bool failed = false)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var documents = Enumerable.Range(0, count).Select(_ => Document.Paste(user, goal, null, Text, DateTime.UtcNow)).ToList();
        if (failed)
        {
            documents.ForEach(document => document.Fail(DocumentFailures.AnalysisFailed));
        }

        db.Documents.AddRange(documents);
        await db.SaveChangesAsync(Cancel);
        return [.. documents.Select(document => document.Id)];
    }

    private static Task<HttpResponseMessage> PasteAsync(HttpClient client) =>
        client.PostAsJsonAsync("/documents", new { text = Text, confirmation = "material-1" }, Cancel);

    private int Count(string user) => Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == user));

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    private HttpClient ClientFor(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";
}