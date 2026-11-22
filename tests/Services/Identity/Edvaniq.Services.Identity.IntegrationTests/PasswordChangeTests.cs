using System.Net;
using System.Net.Http.Headers;
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

namespace Edvaniq.Services.Identity.IntegrationTests;

// #254: POST /accounts/password with a user's token. Every test uses account names of its own, and every factory has a
// LoginLock of its own, so the lock of one test never meets another.
public sealed class PasswordChangeTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private const string Path = "/accounts/password";
    private static readonly string Password = NewPassword();

    [Fact]
    public async Task RightPassword_IsNoContent_AndOnlyTheNewOneLogsIn()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "changing");
        var newPassword = NewPassword();

        using var response = await ChangeAsync(identity, await LoginTokenAsync(identity, "changing"), Password, newPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var old = await identity.LoginAsync("changing", Password);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        using var current = await identity.LoginAsync("changing", newPassword);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task UserAccount_ChangesItsPassword_AndLogsInByAddressWithTheNewOne()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = $"anna-{Guid.NewGuid():N}@example.org";
        await AddUserAsync(identity, email);
        var newPassword = NewPassword();

        using var response = await ChangeAsync(identity, await LoginTokenAsync(identity, email), Password, newPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var old = await identity.LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        using var current = await identity.LoginAsync(email, newPassword);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    // The token names the account. Whatever the body says about another one is not read.
    [Fact]
    public async Task Subject_DecidesTheAccount_NotTheBody()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var mineId = await CreateAccountAsync(identity, "mine");
        var otherId = await CreateAccountAsync(identity, "other");
        var otherHash = await HashOfAsync(identity, otherId);
        var newPassword = NewPassword();
        using var client = ClientWith(identity, await LoginTokenAsync(identity, "mine"));

        using var response = await client.PostAsJsonAsync(
            Path,
            new { currentPassword = Password, newPassword, name = "other", id = otherId, accountId = otherId, sub = otherId },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(otherHash, await HashOfAsync(identity, otherId));
        Assert.True(PasswordHasher.Verify(newPassword, await HashOfAsync(identity, mineId)));
        using var other = await identity.LoginAsync("other", Password);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Theory]
    [InlineData("a wrong password")]
    [InlineData("")]
    public async Task WrongCurrentPassword_IsCurrentPasswordInvalid_AndKeepsTheHash(string current)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var id = await CreateAccountAsync(identity, "wrongcurrent");
        var hash = await HashOfAsync(identity, id);

        using var response = await ChangeAsync(identity, await LoginTokenAsync(identity, "wrongcurrent"), current, NewPassword());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "current_password_invalid", "Dein aktuelles Passwort stimmt nicht.");
        Assert.Equal(hash, await HashOfAsync(identity, id));
    }

    [Theory]
    [InlineData(11, "Dein Passwort braucht mindestens 12 Zeichen.")]
    [InlineData(0, "Dein Passwort braucht mindestens 12 Zeichen.")]
    [InlineData(257, "Dein Passwort darf höchstens 256 Zeichen haben.")]
    public async Task NewPasswordBreakingTheRules_IsPasswordRules_AndKeepsTheHash(int length, string title)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var id = await CreateAccountAsync(identity, "rules");
        var hash = await HashOfAsync(identity, id);

        using var response = await ChangeAsync(identity, await LoginTokenAsync(identity, "rules"), Password, new string('x', length));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "password_rules", title);
        Assert.Equal(hash, await HashOfAsync(identity, id));
        using var login = await identity.LoginAsync("rules", Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task EmptyBody_IsAWrongPassword()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "empty");
        using var client = ClientWith(identity, await LoginTokenAsync(identity, "empty"));

        using var response = await client.PostAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "current_password_invalid", "Dein aktuelles Passwort stimmt nicht.");
    }

    // MaxFailures wrong current passwords lock the change, even with the right one, and the login of the account with it:
    // the key is the same. The wrong ones of the login count the same way.
    [Fact]
    public async Task WrongCurrentPasswords_LockTheLogin_AlsoForTheRightOne()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var id = await CreateAccountAsync(identity, "locked");
        var hash = await HashOfAsync(identity, id);
        var token = await LoginTokenAsync(identity, "locked");

        for (var attempt = 0; attempt < LoginLock.MaxFailures; attempt++)
        {
            using var wrong = await ChangeAsync(identity, token, "a wrong password", NewPassword());
            await AssertProblemAsync(wrong, HttpStatusCode.BadRequest, "current_password_invalid", "Dein aktuelles Passwort stimmt nicht.");
        }

        using var locked = await ChangeAsync(identity, token, Password, NewPassword());
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(LoginLock.LockedFor, locked.Headers.RetryAfter?.Delta);
        Assert.Equal(hash, await HashOfAsync(identity, id));
        using var login = await identity.LoginAsync("locked", Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, login.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordsAtTheLogin_CountForTheChange()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "mixed");
        var token = await LoginTokenAsync(identity, "mixed");
        for (var attempt = 0; attempt < LoginLock.MaxFailures - 1; attempt++)
        {
            using var wrong = await identity.LoginAsync("mixed", "a wrong password");
        }

        using var last = await ChangeAsync(identity, token, "a wrong password", NewPassword());
        using var locked = await ChangeAsync(identity, token, Password, NewPassword());

        Assert.Equal(HttpStatusCode.BadRequest, last.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task LockedByAddress_ForAUserAccount()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = $"locked-{Guid.NewGuid():N}@example.org";
        await AddUserAsync(identity, email);
        var token = await LoginTokenAsync(identity, email);

        for (var attempt = 0; attempt < LoginLock.MaxFailures; attempt++)
        {
            using var wrong = await ChangeAsync(identity, token, "a wrong password", NewPassword());
        }

        using var locked = await ChangeAsync(identity, token, Password, NewPassword());
        using var login = await identity.LoginAsync(email.ToUpperInvariant(), Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, login.StatusCode);
    }

    // A change that went through does not clear what the wrong ones before it added up to.
    [Fact]
    public async Task ASuccess_DoesNotResetTheCount()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await CreateAccountAsync(identity, "nobreather");
        var token = await LoginTokenAsync(identity, "nobreather");
        for (var attempt = 0; attempt < LoginLock.MaxFailures - 1; attempt++)
        {
            using var wrong = await ChangeAsync(identity, token, "a wrong password", NewPassword());
        }

        var newPassword = NewPassword();
        using var changed = await ChangeAsync(identity, token, Password, newPassword);
        using var last = await ChangeAsync(identity, token, "a wrong password", NewPassword());
        using var locked = await ChangeAsync(identity, token, newPassword, NewPassword());

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, last.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task AccountThatIsGone_IsNotFound()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var id = await CreateAccountAsync(identity, "vanishing");
        var token = await LoginTokenAsync(identity, "vanishing");
        using (var scope = identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Accounts.Where(account => account.Id == Guid.Parse(id)).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        using var response = await ChangeAsync(identity, token, Password, NewPassword());

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "account_not_found", "Dieses Konto gibt es nicht mehr.");
    }

    // A token signed with the previous key stays valid while the key is changed, like with every service.
    [Fact]
    public async Task TokenOfThePreviousKey_StillWorks_WhileTheKeyIsChanged()
    {
        var previousKey = IdentityFactory.NewSigningKey();
        await using var before = new IdentityFactory(
            new Dictionary<string, string?> { ["Tokens:SigningKey"] = previousKey }, connectionString: database.ConnectionString);
        await CreateAccountAsync(before, "keychange");
        var token = await LoginTokenAsync(before, "keychange");

        await using var after = new IdentityFactory(
            new Dictionary<string, string?> { ["Tokens:SigningKey"] = IdentityFactory.NewSigningKey(), ["Tokens:PreviousSigningKey"] = previousKey },
            connectionString: database.ConnectionString);
        using var response = await ChangeAsync(after, token, Password, NewPassword());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    internal static HttpClient ClientWith(IdentityFactory identity, string? token)
    {
        var client = identity.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static Task<HttpResponseMessage> ChangeAsync(IdentityFactory identity, string token, string current, string next)
    {
        var client = ClientWith(identity, token);
        return client.PostAsJsonAsync(Path, new { currentPassword = current, newPassword = next }, TestContext.Current.CancellationToken);
    }

    private static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    private static async Task<string> CreateAccountAsync(IdentityFactory identity, string name)
    {
        var (exitCode, output, error) = await identity.SetAccountAsync(name, Password);
        Assert.True(exitCode == 0, error);
        return output.Trim().Split(' ')[^1];
    }

    private static async Task AddUserAsync(IdentityFactory identity, string email)
    {
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = Account.CreateUser(AccountEmail.Normalize(email)!, "Anna", 2001, PasswordHasher.Hash(Password), DateTime.UtcNow);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await db.Accounts.Where(item => item.Id == account.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, AccountStatus.Active), TestContext.Current.CancellationToken);
    }

    private static async Task<string> LoginTokenAsync(IdentityFactory identity, string login)
    {
        using var response = await identity.LoginAsync(login, Password);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task<string> HashOfAsync(IdentityFactory identity, string id)
    {
        using var scope = identity.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Accounts.AsNoTracking()
            .Where(account => account.Id == Guid.Parse(id))
            .Select(account => account.PasswordHash)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string title)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((code, title), (problem.GetProperty("code").GetString(), problem.GetProperty("title").GetString()));
    }
}

// The token check of the endpoint needs no database: it answers before anything of the account is looked at.
public sealed class PasswordChangeTokenTests
{
    private const string Path = "/accounts/password";

    [Fact]
    public async Task WithoutAToken_IsUnauthorized()
    {
        await using var identity = new IdentityFactory();

        using var response = await PasswordChangeTests.ClientWith(identity, null)
            .PostAsJsonAsync(Path, new { currentPassword = "x", newPassword = "y" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenThatIsNone_IsUnauthorized()
    {
        await using var identity = new IdentityFactory();

        using var response = await Post(identity, "not.a.token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A client's own token has the right issuer, audience and key, but no account behind it.
    [Fact]
    public async Task ClientToken_IsForbidden()
    {
        await using var identity = new IdentityFactory();

        using var response = await Post(identity, await identity.GetTokenAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The token Content's worker gets is for the internal audience, so it is no token for the services at all.
    [Fact]
    public async Task InternalClientToken_IsUnauthorized()
    {
        await using var identity = new IdentityFactory();
        using var tokenResponse = await identity.RequestTokenAsync(clientId: IdentityFactory.InternalClientId, clientSecret: identity.InternalClientSecret);
        var body = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        using var response = await Post(identity, body.GetProperty("access_token").GetString()!);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A token of someone else's key, with a real account as subject.
    [Fact]
    public async Task TokenOfAnotherKey_IsUnauthorized()
    {
        await using var identity = new IdentityFactory();
        await using var stranger = new IdentityFactory();
        var token = stranger.Services.GetRequiredService<TokenIssuer>().IssueForAccount(Guid.NewGuid(), false, IdentityFactory.ClientId);

        using var response = await Post(identity, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Issuer and audience are checked as everywhere: a token of the same key for another issuer does not pass.
    [Fact]
    public async Task TokenOfAnotherIssuer_IsUnauthorized()
    {
        var key = IdentityFactory.NewSigningKey();
        await using var identity = new IdentityFactory(new Dictionary<string, string?> { ["Tokens:SigningKey"] = key });
        await using var other = new IdentityFactory(new Dictionary<string, string?> { ["Tokens:SigningKey"] = key, ["Tokens:Issuer"] = "someone-else" });
        var token = other.Services.GetRequiredService<TokenIssuer>().IssueForAccount(Guid.NewGuid(), false, IdentityFactory.ClientId);

        using var response = await Post(identity, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The endpoints that stay open need no token.
    [Fact]
    public async Task OpenEndpoints_StayOpen()
    {
        await using var identity = new IdentityFactory();
        using var client = identity.CreateClient();

        using var keys = await client.GetAsync("/.well-known/jwks", TestContext.Current.CancellationToken);
        using var versions = await client.GetAsync("/legal-versions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, keys.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
    }

    private static Task<HttpResponseMessage> Post(IdentityFactory identity, string token) =>
        PasswordChangeTests.ClientWith(identity, token)
            .PostAsJsonAsync(Path, new { currentPassword = "x", newPassword = "y" }, TestContext.Current.CancellationToken);
}
