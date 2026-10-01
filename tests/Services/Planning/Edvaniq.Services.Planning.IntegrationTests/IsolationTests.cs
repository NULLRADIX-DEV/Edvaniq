using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Edvaniq.Testing;

namespace Edvaniq.Services.Planning.IntegrationTests;

// User A's data stays invisible to user B, because the owner filter of ServiceDbContext limits every query to the user
// from the token. Shown with the example items, against a real database.
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

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(alice, $"/examples/{item.Id}"));
        // A foreign item looks exactly like one that does not exist.
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/examples/{item.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/examples/{Guid.NewGuid()}"));
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
        using var response = await client.PostAsJsonAsync("/examples", new { text }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Item>(cancellationToken))!;
    }

    private static async Task<Item[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Item[]>("/examples", TestContext.Current.CancellationToken))!;

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private sealed record Item(Guid Id, string Text);
}
