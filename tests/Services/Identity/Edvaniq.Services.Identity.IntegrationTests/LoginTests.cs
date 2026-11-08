using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Edvaniq.Services.Identity.Api.Tokens;

using Microsoft.IdentityModel.JsonWebTokens;

namespace Edvaniq.Services.Identity.IntegrationTests;

// The login of a team account: POST /connect/token with grant_type=password from the web host (docs/tokens.md). Every
// test uses an account name of its own, so the throttle of one test never meets another.
public sealed class LoginTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static readonly string Password = NewPassword();

    [Fact]
    public async Task RightPassword_GetsAToken_ForTheAccount_ThatIsNotCached()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var accountId = await CreateAccountAsync(identity, "right");

        using var response = await identity.LoginAsync("right", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
        Assert.Equal(3600, body.GetProperty("expires_in").GetInt32());
        var token = new JsonWebToken(body.GetProperty("access_token").GetString());
        Assert.Equal(accountId, token.Subject);
    }

    // The token of the contract: same issuer, audience, key and type as a client's own, only subject and lifetime differ.
    [Fact]
    public async Task Token_HasTheShapeOfTheContract()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var accountId = await CreateAccountAsync(identity, "contract");

        var token = new JsonWebToken(await LoginTokenAsync(identity, "contract"));
        var clientToken = new JsonWebToken(await identity.GetTokenAsync());

        Assert.Equal(IdentityFactory.Issuer, token.Issuer);
        Assert.Equal(["edvaniq-api"], token.Audiences);
        Assert.Equal(accountId, token.Subject);
        Assert.Equal(IdentityFactory.ClientId, token.GetClaim("client_id").Value);
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.Equal("ES256", token.Alg);
        Assert.Equal("at+jwt", token.Typ);
        Assert.Equal(clientToken.Kid, token.Kid);
        Assert.Equal(TimeSpan.FromHours(1), token.ValidTo - token.IssuedAt);
        Assert.Equal(
            ["aud", "client_id", "exp", "iat", "iss", "jti", "nbf", "sub"],
            token.Claims.Select(claim => claim.Type).Order(StringComparer.Ordinal));
    }

    // "Ben" and " ben " are the account "ben".
    [Theory]
    [InlineData("Casing")]
    [InlineData(" casing ")]
    [InlineData("CASING")]
    public async Task Name_IsNormalized_LikeWhenCreated(string name)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var accountId = await CreateAccountAsync(identity, "casing");

        using var response = await identity.LoginAsync(name, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(accountId, new JsonWebToken(body.GetProperty("access_token").GetString()).Subject);
    }

    // A wrong password and an unknown name get the same answer, so nobody learns which accounts exist.
    [Theory]
    [InlineData("wrong", "the wrong password")]
    [InlineData("nobody-here", null)]
    [InlineData("not a name!", null)]
    public async Task WrongNameOrPassword_IsInvalidGrant(string name, string? password)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "wrong");

        using var response = await identity.LoginAsync(name, password ?? Password);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_grant");
    }

    [Fact]
    public async Task OldPassword_StopsWorking_AfterTheCommandSetsANewOne()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var accountId = await CreateAccountAsync(identity, "reset");
        var newPassword = NewPassword();

        var (exitCode, output, _) = await identity.SetAccountAsync("reset", newPassword);

        Assert.Equal(0, exitCode);
        Assert.Equal($"updated reset {accountId}", output.Trim());
        using var old = await identity.LoginAsync("reset", Password);
        await AssertErrorAsync(old, HttpStatusCode.BadRequest, "invalid_grant");
        using var current = await identity.LoginAsync("reset", newPassword);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    // Only a client that may log accounts in gets a token for one. Today that is web alone.
    [Fact]
    public async Task ClientWithoutPasswordGrant_IsUnauthorizedClient()
    {
        var toolSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var identity = new IdentityFactory(
            new Dictionary<string, string?> { ["Tokens:Clients:tool:Secret"] = toolSecret },
            connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "unauthorized");

        using var response = await identity.LoginAsync("unauthorized", Password, clientId: "tool", clientSecret: toolSecret);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "unauthorized_client");
    }

    [Theory]
    [InlineData("web", "wrong-secret")]
    [InlineData("someone-else", null)]
    public async Task WrongClient_IsInvalidClient_EvenWithTheRightPassword(string clientId, string? clientSecret)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "client");

        using var response = await identity.LoginAsync("client", Password, clientId, clientSecret);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_client");
    }

    [Theory]
    [InlineData("", "a password")]
    [InlineData("someone", "")]
    public async Task MissingNameOrPassword_IsInvalidRequest(string name, string password)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        using var response = await identity.LoginAsync(name, password);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    // After AttemptsPerMinute tries the account is closed for the rest of the minute, even for the right password, and
    // another spelling of the name counts against the same account.
    [Fact]
    public async Task TooManyAttempts_AreThrottled_PerNormalizedName()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "throttled");
        await CreateAccountAsync(identity, "bystander");

        for (var attempt = 0; attempt < LoginThrottle.AttemptsPerMinute; attempt++)
        {
            using var wrong = await identity.LoginAsync(attempt % 2 == 0 ? "throttled" : "Throttled", "the wrong password");
            await AssertErrorAsync(wrong, HttpStatusCode.BadRequest, "invalid_grant");
        }

        using var throttled = await identity.LoginAsync(" THROTTLED ", Password);
        await AssertErrorAsync(throttled, HttpStatusCode.TooManyRequests, "too_many_attempts");
        Assert.NotNull(throttled.Headers.RetryAfter);

        using var other = await identity.LoginAsync("bystander", Password);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    private static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    private static async Task<string> CreateAccountAsync(IdentityFactory identity, string name)
    {
        var (exitCode, output, error) = await identity.SetAccountAsync(name, Password);
        Assert.True(exitCode == 0, error);
        return output.Trim().Split(' ')[^1];
    }

    private static async Task<string> LoginTokenAsync(IdentityFactory identity, string name)
    {
        using var response = await identity.LoginAsync(name, Password);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(error, body.GetProperty("error").GetString());
    }
}