using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.IdentityModel.JsonWebTokens;

namespace Edvaniq.Services.Identity.IntegrationTests;

// A service finds the public key the way OpenID Connect describes it: the discovery document under its Authority names
// the key set. Only the public half of the key ever leaves Identity.
public sealed class KeyTests
{
    [Fact]
    public async Task Discovery_NamesIssuerKeysAndTokenEndpoint()
    {
        await using var identity = new IdentityFactory();
        using var client = identity.CreateClient();

        var discovery = await client.GetFromJsonAsync<JsonElement>(
            "/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal(IdentityFactory.Issuer, discovery.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost/.well-known/jwks", discovery.GetProperty("jwks_uri").GetString());
        Assert.Equal("http://localhost/connect/token", discovery.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public async Task KeySet_HasThePublicKeyOnly()
    {
        await using var identity = new IdentityFactory();
        using var client = identity.CreateClient();

        var keySet = await client.GetFromJsonAsync<JsonElement>("/.well-known/jwks", TestContext.Current.CancellationToken);

        var key = Assert.Single(keySet.GetProperty("keys").EnumerateArray());
        Assert.Equal("EC", key.GetProperty("kty").GetString());
        Assert.Equal("P-256", key.GetProperty("crv").GetString());
        Assert.Equal("sig", key.GetProperty("use").GetString());
        Assert.Equal("ES256", key.GetProperty("alg").GetString());
        Assert.False(key.TryGetProperty("d", out _), "The key set must not contain the private key.");
    }

    // The service picks the key by the kid in the token's header.
    [Fact]
    public async Task Token_NamesTheKeyFromTheKeySet()
    {
        await using var identity = new IdentityFactory();
        using var client = identity.CreateClient();

        var keySet = await client.GetFromJsonAsync<JsonElement>("/.well-known/jwks", TestContext.Current.CancellationToken);
        var token = new JsonWebToken(await identity.GetTokenAsync());

        Assert.Equal(keySet.GetProperty("keys")[0].GetProperty("kid").GetString(), token.Kid);
    }

    [Fact]
    public async Task SameSigningKey_GivesTheSameKeyId()
    {
        var signingKey = IdentityFactory.NewSigningKey();
        await using var first = new IdentityFactory(new Dictionary<string, string?> { ["Tokens:SigningKey"] = signingKey });
        await using var second = new IdentityFactory(new Dictionary<string, string?> { ["Tokens:SigningKey"] = signingKey });

        Assert.Equal(new JsonWebToken(await first.GetTokenAsync()).Kid, new JsonWebToken(await second.GetTokenAsync()).Kid);
    }

    [Fact]
    public async Task Health_StillAnswers()
    {
        await using var identity = new IdentityFactory();
        using var client = identity.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}