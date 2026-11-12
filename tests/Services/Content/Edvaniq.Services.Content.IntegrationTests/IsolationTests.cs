using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Edvaniq.Testing;

namespace Edvaniq.Services.Content.IntegrationTests;

// User A's data stays invisible to user B, because the owner filter of ServiceDbContext limits every query to the user
// from the token. Shown with pasted documents, against a real database.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
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
        await Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<Infrastructure.DocumentSplitter>(service.Services)
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
        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Note(text) }, cancellationToken);
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
