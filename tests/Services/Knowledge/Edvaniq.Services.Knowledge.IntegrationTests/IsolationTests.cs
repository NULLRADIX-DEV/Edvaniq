using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// User A's data stays invisible to user B, because the owner filter of ServiceDbContext limits every query to the user
// from the token. Shown with topics, against a real database.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task OtherUser_DoesNotSeeTheItemsInTheList()
    {
        var alice = NewUser("alice");
        using var aliceClient = ClientFor(alice);
        using var bob = ClientFor(NewUser("bob"));

        var topic = await service.AddTopicAsync(alice, "Zellbiologie");

        Assert.Contains(await ListAsync(aliceClient), listed => listed.Id == topic.Id);
        Assert.Empty(await ListAsync(bob));
    }

    [Fact]
    public async Task OtherUser_GetsNotFoundForAForeignItem()
    {
        var alice = NewUser("alice");
        using var aliceClient = ClientFor(alice);
        using var bob = ClientFor(NewUser("bob"));

        var topic = await service.AddTopicAsync(alice, "Zellbiologie");

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(aliceClient, $"/topics/{topic.Id}"));
        // A foreign item looks exactly like one that does not exist.
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/topics/{topic.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/topics/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task OtherUser_CannotRenameAForeignItem()
    {
        var alice = NewUser("alice");
        using var aliceClient = ClientFor(alice);
        using var bob = ClientFor(NewUser("bob"));

        var topic = await service.AddTopicAsync(alice, "Zellbiologie");

        using var response = await bob.PatchAsJsonAsync($"/topics/{topic.Id}", new { name = "Von Bob" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = await aliceClient.GetFromJsonAsync<Item>($"/topics/{topic.Id}", TestContext.Current.CancellationToken);
        Assert.Equal("Zellbiologie", unchanged!.Name);
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheItems()
    {
        var id = Guid.NewGuid().ToString("N");
        using var lower = ClientFor($"alice-{id}");
        using var upper = ClientFor($"Alice-{id}");

        var topic = await service.AddTopicAsync($"alice-{id}", "Zellbiologie");

        Assert.Empty(await ListAsync(upper));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(lower, $"/topics/{topic.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(upper, $"/topics/{topic.Id}"));
    }

    // Every test has its own users, so the tests of this class can share one database.
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Item[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Item[]>("/topics", TestContext.Current.CancellationToken))!;

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private sealed record Item(Guid Id, string Name);
}