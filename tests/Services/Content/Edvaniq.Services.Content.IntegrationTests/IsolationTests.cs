using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// User A's data stays invisible to user B, because the owner filter of ServiceDbContext limits every query to the user
// from the token. Every endpoint with a user's token is listed here (IsolationCheck, #271), against a real database.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    internal static IsolationCheck Check() => new IsolationCheck()
        .Exempt("GET", "/me", "Only the caller's own id from the token.")
        .List("GET", "/documents")
        .Item("GET", "/documents/{id:guid}")
        .Item("GET", "/documents/{id:guid}/sections")
        .Item("POST", "/documents/{id:guid}/retry")
        .Item("DELETE", "/documents/{id:guid}")
        .Exempt("POST", "/documents", "Adds a document of the caller, a foreign goal is Planning's: LearningGoalTests.")
        .Exempt("POST", "/documents/files", "Adds a document of the caller, a foreign goal is Planning's: LearningGoalTests.")
        .Exempt("GET", "/quota", "Only the caller's own count: OtherUser_HasTheirOwnQuota.")
        .Exempt("GET", "/topics/{topicId:guid}/summaries/{length}", "Two parameters, checked by SummaryTests.OtherUser_NeitherReadsNorRequestsASummary.")
        .Exempt("POST", "/topics/{topicId:guid}/summaries/{length}", "Two parameters, checked by SummaryTests.OtherUser_NeitherReadsNorRequestsASummary.");

    [Fact]
    public void EveryEndpoint_IsInTheCheck() => Check().AssertCovers(service.Services);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorChangeTheDocuments() => Check().RunAsync(service, SeedAsync);

    [Fact]
    public async Task OtherUser_DoesNotSeeTheItemsInTheList()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));

        var item = await CreateAsync(alice, "Notiz von Alice");

        Assert.Contains(await ListAsync(alice), listed => listed.Id == item.Id);
        Assert.Empty(await ListAsync(bob));
    }

    [Fact]
    public async Task OtherUser_GetsNotFoundForAForeignItem()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));

        var item = await CreateAsync(alice, "Notiz von Alice");

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(alice, $"/documents/{item.Id}"));
        // A foreign item looks exactly like one that does not exist.
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/documents/{item.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/documents/{Guid.NewGuid()}"));
    }

    // #301: the quota counts the caller's own pages only, Alice's document leaves Bob's count alone.
    [Fact]
    public async Task OtherUser_HasTheirOwnQuota()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));
        var before = await bob.GetStringAsync("/quota", TestContext.Current.CancellationToken);

        var item = await CreateAsync(alice, "Notiz von Alice");
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services)
            .SplitAsync(item.Id, TestContext.Current.CancellationToken);

        Assert.NotEqual(before, await alice.GetStringAsync("/quota", TestContext.Current.CancellationToken));
        Assert.Equal(before, await bob.GetStringAsync("/quota", TestContext.Current.CancellationToken));
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheItems()
    {
        var id = Guid.NewGuid().ToString("N");
        using var lower = ClientFor($"alice-{id}");
        using var upper = ClientFor($"Alice-{id}");

        var item = await CreateAsync(lower, "Notiz von alice");

        Assert.Empty(await ListAsync(upper));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(lower, $"/documents/{item.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(upper, $"/documents/{item.Id}"));
    }

    // Every test has its own users, so the tests of this class can share one database.
    // A document of A, failed so a retry would start it, with a title only A has.
    private async Task<OwnedResource> SeedAsync(string owner)
    {
        var title = $"Skript {Guid.NewGuid():N}";
        var document = Document.Paste(owner, Guid.NewGuid(), title, TestTexts.Lesson, DateTime.UtcNow);
        document.Fail(DocumentFailures.AnalysisFailed);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        db.Documents.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new OwnedResource(document.Id, $"/documents/{document.Id}", title);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Item> CreateAsync(HttpClient client, string text)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Note(text), confirmation = "material-1" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Item>(cancellationToken))!;
    }

    private static async Task<Item[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Item[]>("/documents", TestContext.Current.CancellationToken))!;

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private sealed record Item(Guid Id, string Title);
}