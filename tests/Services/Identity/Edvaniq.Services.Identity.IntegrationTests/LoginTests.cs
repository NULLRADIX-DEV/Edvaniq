using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Edvaniq.Services.Identity.Api.Tokens;
using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Services.Identity.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

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

    // #225: only an account the operator set with --test-double gets test_double in its tokens. Setting it again
    // without the option takes the mark away, and an ordinary account never carries the claim.
    [Fact]
    public async Task TestDoubleAccount_GetsTheClaim_OnlyWhileMarked()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var ordinaryId = await CreateAccountAsync(identity, "ordinary");
        var (exitCode, output, _) = await identity.SetAccountAsync("tester", Password, testDouble: true);

        var marked = new JsonWebToken(await LoginTokenAsync(identity, "tester"));
        var ordinary = new JsonWebToken(await LoginTokenAsync(identity, "ordinary"));
        await identity.SetAccountAsync("tester", Password);
        var unmarked = new JsonWebToken(await LoginTokenAsync(identity, "tester"));

        Assert.Equal(0, exitCode);
        Assert.EndsWith(" --test-double", output.Trim(), StringComparison.Ordinal);
        Assert.True(marked.TryGetPayloadValue<bool>(TokenIssuer.TestDoubleClaim, out var value) && value);
        Assert.Equal(ordinaryId, ordinary.Subject);
        Assert.False(ordinary.TryGetPayloadValue<object>(TokenIssuer.TestDoubleClaim, out _));
        Assert.False(unmarked.TryGetPayloadValue<object>(TokenIssuer.TestDoubleClaim, out _));
        Assert.Equal(marked.Subject, unmarked.Subject);
    }

    // #244: a user account logs in with its address and gets a token of the same contract as a team account: same claims,
    // its id as sub, nothing of the person in it.
    [Fact]
    public async Task UserAccount_LogsInWithItsAddress_AndItsTokenNamesNoPerson()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = $"anna-{Guid.NewGuid():N}@example.org";
        var accountId = await AddUserAsync(identity, email, AccountStatus.Active);
        await CreateAccountAsync(identity, "teammate");
        var teamToken = new JsonWebToken(await LoginTokenAsync(identity, "teammate"));

        var token = new JsonWebToken(await LoginTokenAsync(identity, " " + email.ToUpperInvariant() + " "));

        Assert.Equal(accountId.ToString(), token.Subject);
        Assert.Equal(teamToken.Claims.Select(claim => claim.Type).Order(StringComparer.Ordinal), token.Claims.Select(claim => claim.Type).Order(StringComparer.Ordinal));
        Assert.Equal((teamToken.Issuer, teamToken.Audiences.Single(), teamToken.Kid), (token.Issuer, token.Audiences.Single(), token.Kid));
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.EncodedPayload));
        Assert.DoesNotContain("anna", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2001", payload, StringComparison.Ordinal);
    }

    // The reason comes only with the right password, the wrong one gets what an unknown address gets.
    [Theory]
    [InlineData(AccountStatus.Unconfirmed, "unconfirmed")]
    [InlineData(AccountStatus.AwaitingApproval, "awaiting_approval")]
    public async Task UserAccountNotActive_GetsItsReason_OnlyWithTheRightPassword(AccountStatus status, string reason)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = $"anna-{Guid.NewGuid():N}@example.org";
        await AddUserAsync(identity, email, status);

        using var right = await identity.LoginAsync(email, Password);
        using var wrong = await identity.LoginAsync(email, "the wrong password");
        using var unknown = await identity.LoginAsync($"nobody-{Guid.NewGuid():N}@example.org", Password);

        var refused = await AssertErrorAsync(right, HttpStatusCode.BadRequest, "invalid_grant");
        Assert.Equal(reason, refused.GetProperty("reason").GetString());
        foreach (var other in new[] { wrong, unknown })
        {
            var body = await AssertErrorAsync(other, HttpStatusCode.BadRequest, "invalid_grant");
            Assert.False(body.TryGetProperty("reason", out _));
        }
    }

    // Spellings of one address share their attempts, another address keeps its own.
    [Fact]
    public async Task TooManyAttempts_AreThrottled_PerAddress()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = $"throttled-{Guid.NewGuid():N}@example.org";
        var other = $"bystander-{Guid.NewGuid():N}@example.org";
        await AddUserAsync(identity, email, AccountStatus.Active);
        await AddUserAsync(identity, other, AccountStatus.Active);

        for (var attempt = 0; attempt < LoginThrottle.AttemptsPerMinute; attempt++)
        {
            using var wrong = await identity.LoginAsync(attempt % 2 == 0 ? email : email.ToUpperInvariant(), "the wrong password");
        }

        using var throttled = await identity.LoginAsync(email, Password);
        await AssertErrorAsync(throttled, HttpStatusCode.TooManyRequests, "too_many_attempts");
        using var bystander = await identity.LoginAsync(other, Password);
        Assert.Equal(HttpStatusCode.OK, bystander.StatusCode);
    }

    // There is no registration yet (#249), so the account goes straight into the database.
    private static async Task<Guid> AddUserAsync(IdentityFactory identity, string email, AccountStatus status)
    {
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = Account.CreateUser(AccountEmail.Normalize(email)!, "Anna", 2001, PasswordHasher.Hash(Password), DateTime.UtcNow);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await db.Accounts.Where(item => item.Id == account.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, status), TestContext.Current.CancellationToken);
        return account.Id;
    }

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

    private static async Task<JsonElement> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(error, body.GetProperty("error").GetString());
        return body;
    }
}