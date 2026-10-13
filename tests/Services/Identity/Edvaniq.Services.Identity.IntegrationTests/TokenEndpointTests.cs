using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.IdentityModel.JsonWebTokens;

namespace Edvaniq.Services.Identity.IntegrationTests;

// POST /connect/token hands out the technical token for a known client (OAuth 2.0 client credentials). The answers
// follow RFC 6749, so a client written against a standard token endpoint keeps working when Identity grows.
public sealed class TokenEndpointTests
{
    [Fact]
    public async Task KnownClient_GetsABearerToken_ThatIsNotCached()
    {
        await using var identity = new IdentityFactory();

        using var response = await identity.RequestTokenAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(300, body.GetProperty("expires_in").GetInt32());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("access_token").GetString()));
    }

    // What every service checks: issuer, audience and expiry. The subject is the client itself, as RFC 9068 says
    // for tokens without a user.
    [Fact]
    public async Task Token_IsForTheApi_WithTheClientAsSubject_AndExpiresAfterFiveMinutes()
    {
        await using var identity = new IdentityFactory();

        var token = new JsonWebToken(await identity.GetTokenAsync());

        Assert.Equal(IdentityFactory.Issuer, token.Issuer);
        Assert.Equal(["edvaniq-api"], token.Audiences);
        Assert.Equal(IdentityFactory.ClientId, token.Subject);
        Assert.Equal(IdentityFactory.ClientId, token.GetClaim("client_id").Value);
        Assert.Equal("ES256", token.Alg);
        Assert.Equal("at+jwt", token.Typ);
        Assert.Equal(TimeSpan.FromMinutes(5), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task EveryToken_HasItsOwnId()
    {
        await using var identity = new IdentityFactory();

        var first = new JsonWebToken(await identity.GetTokenAsync());
        var second = new JsonWebToken(await identity.GetTokenAsync());

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("web", "wrong-secret")]
    [InlineData("web", "")]
    [InlineData("someone-else", null)]
    public async Task WrongClientOrSecret_IsInvalidClient(string clientId, string? clientSecret)
    {
        await using var identity = new IdentityFactory();

        using var response = await identity.RequestTokenAsync(clientId: clientId, clientSecret: clientSecret);

        await AssertErrorAsync(response, "invalid_client");
    }

    [Fact]
    public async Task OtherGrant_IsUnsupported()
    {
        await using var identity = new IdentityFactory();

        using var response = await identity.RequestTokenAsync(grantType: "password");

        await AssertErrorAsync(response, "unsupported_grant_type");
    }

    [Fact]
    public async Task RequestWithoutForm_IsInvalid()
    {
        await using var identity = new IdentityFactory();
        using var client = identity.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/connect/token",
            new { grant_type = "client_credentials", client_id = IdentityFactory.ClientId, client_secret = identity.ClientSecret },
            TestContext.Current.CancellationToken);

        await AssertErrorAsync(response, "invalid_request");
    }

    // A form ASP.NET Core cannot read, with more fields than it allows or a broken multipart body, is a bad request of
    // the client, not an error of Identity.
    [Theory]
    [InlineData("application/x-www-form-urlencoded", "many")]
    [InlineData("multipart/form-data; boundary=x", "--x\r\nbroken")]
    public async Task UnreadableForm_IsInvalid(string contentType, string body)
    {
        await using var identity = new IdentityFactory();
        using var client = identity.CreateClient();
        using var content = new StringContent(body == "many" ? string.Join("&", Enumerable.Repeat("a=1", 2000)) : body);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);

        using var response = await client.PostAsync("/connect/token", content, TestContext.Current.CancellationToken);

        await AssertErrorAsync(response, "invalid_request");
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, string error)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(error, body.GetProperty("error").GetString());
    }
}