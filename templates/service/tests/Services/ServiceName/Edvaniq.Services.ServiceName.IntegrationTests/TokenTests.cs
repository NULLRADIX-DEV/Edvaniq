using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Edvaniq.Testing;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

// Every request needs a valid token, and the user comes only from it (GET /me answers with the id from the token).
public sealed class TokenTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private const string Alice = "alice";

    [Fact]
    public async Task Request_WithoutToken_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(token: null)).StatusCode);

    [Fact]
    public async Task Request_WithMalformedToken_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync("not-a-token")).StatusCode);

    [Fact]
    public async Task Request_WithTokenSignedByOtherKey_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(TestTokens.Create(Alice, signingKey: TestTokens.NewKey()))).StatusCode);

    [Fact]
    public async Task Request_WithTokenFromOtherIssuer_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(TestTokens.Create(Alice, issuer: "someone-else"))).StatusCode);

    [Fact]
    public async Task Request_WithTokenForOtherAudience_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(TestTokens.Create(Alice, audience: "other-app"))).StatusCode);

    [Fact]
    public async Task Request_WithExpiredToken_IsRejected() =>
        // Past the 5 minutes .NET tolerates by default for clocks that run apart.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(TestTokens.Create(Alice, expires: DateTime.UtcNow.AddMinutes(-6)))).StatusCode);

    [Fact]
    public async Task Request_WithTokenWithoutUser_IsForbidden() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await GetMeAsync(TestTokens.Create(userId: null))).StatusCode);

    [Fact]
    public async Task Request_WithValidToken_ReturnsUserFromToken()
    {
        using var response = await GetMeAsync(TestTokens.Create(Alice));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Alice, await ReadUserIdAsync(response));
    }

    [Fact]
    public async Task Request_WithForeignUserId_ReturnsUserFromToken()
    {
        using var response = await GetMeAsync(TestTokens.Create(Alice), "/me?userId=bob&sub=bob", request =>
        {
            request.Headers.Add("X-User-Id", "bob");
            request.Content = JsonContent.Create(new { userId = "bob" });
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Alice, await ReadUserIdAsync(response));
    }

    private async Task<HttpResponseMessage> GetMeAsync(string? token, string path = "/me", Action<HttpRequestMessage>? configure = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        configure?.Invoke(request);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> ReadUserIdAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Me>(TestContext.Current.CancellationToken))?.Id;

    private sealed record Me(string Id);
}
