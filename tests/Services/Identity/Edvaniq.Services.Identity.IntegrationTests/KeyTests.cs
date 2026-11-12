using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.Services.Identity.IntegrationTests;

// A service finds the public key the way OpenID Connect describes it: the discovery document under its Authority names
// the key set. Only the public half of the key ever leaves Identity.
public sealed class KeyTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
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

    // #246, the change of the key as the operator does it: the new key signs, the old one stays in the key set as
    // previous, so a token from before stays valid until it runs out. Once the previous key is gone, so is that token.
    [Fact]
    public async Task KeyChange_KeepsTokensOfThePreviousKey_UntilItIsTakenOut()
    {
        var (oldKey, newKey) = (IdentityFactory.NewSigningKey(), IdentityFactory.NewSigningKey());
        await using var before = new IdentityFactory(new Dictionary<string, string?> { ["Tokens:SigningKey"] = oldKey });
        await using var during = new IdentityFactory(new Dictionary<string, string?>
        {
            ["Tokens:SigningKey"] = newKey,
            ["Tokens:PreviousSigningKey"] = oldKey,
        });
        await using var after = new IdentityFactory(new Dictionary<string, string?> { ["Tokens:SigningKey"] = newKey });
        var oldToken = await before.GetTokenAsync();
        var newToken = await during.GetTokenAsync();

        var keysDuring = await KeySetAsync(during);
        var keysAfter = await KeySetAsync(after);

        Assert.Equal(
            [new JsonWebToken(newToken).Kid, new JsonWebToken(oldToken).Kid],
            keysDuring.Keys.Select(key => key.Kid));
        Assert.All(keysDuring.Keys, key => Assert.Null(key.D));
        Assert.True(await IsValidAsync(oldToken, keysDuring));
        Assert.True(await IsValidAsync(newToken, keysDuring));
        Assert.True(await IsValidAsync(newToken, keysAfter));
        Assert.False(await IsValidAsync(oldToken, keysAfter));
    }

    private static async Task<JsonWebKeySet> KeySetAsync(IdentityFactory identity)
    {
        using var client = identity.CreateClient();
        return new JsonWebKeySet(await client.GetStringAsync("/.well-known/jwks", TestContext.Current.CancellationToken));
    }

    // As a service checks a token: issuer, audience, lifetime and a key from the key set.
    private static async Task<bool> IsValidAsync(string token, JsonWebKeySet keys) =>
        (await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = IdentityFactory.Issuer,
            ValidAudience = "edvaniq-api",
            IssuerSigningKeys = keys.GetSigningKeys(),
        })).IsValid;

    [Fact]
    public async Task Health_StillAnswers()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        using var client = identity.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}