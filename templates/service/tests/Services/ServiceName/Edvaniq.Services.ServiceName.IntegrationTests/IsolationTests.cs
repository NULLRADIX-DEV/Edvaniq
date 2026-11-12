using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Edvaniq.Testing;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

// User A's data stays invisible to user B, because the owner filter of ServiceDbContext limits every query to the user
// from the token. Every endpoint with a user's token is listed here (IsolationCheck), against a real database: a new
// endpoint fails EveryEndpoint_IsInTheCheck until it is listed.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static IsolationCheck Check() => new IsolationCheck()
        .Exempt("GET", "/me", "Only the caller's own id from the token.")
        .Exempt("POST", "/examples", "Creates an item of the caller, names no existing one.")
        .List("GET", "/examples")
        .Item("GET", "/examples/{id:guid}");

    [Fact]
    public void EveryEndpoint_IsInTheCheck() => Check().AssertCovers(service.Services);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorChangeTheItems() => Check().RunAsync(service, SeedAsync);

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheItems()
    {
        var id = Guid.NewGuid().ToString("N");
        using var lower = ClientFor($"alice-{id}");
        using var upper = ClientFor($"Alice-{id}");

        var item = await CreateAsync(lower, "Notiz von alice");

        using var response = await upper.GetAsync($"/examples/{item.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<OwnedResource> SeedAsync(string owner)
    {
        using var client = ClientFor(owner);
        var item = await CreateAsync(client, $"Notiz {Guid.NewGuid():N}");
        return new OwnedResource(item.Id, $"/examples/{item.Id}", item.Text);
    }

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

    private sealed record Item(Guid Id, string Text);
}
