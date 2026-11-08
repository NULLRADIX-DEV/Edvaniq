using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

using Edvaniq.Services.Identity.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Identity.IntegrationTests;

// Identity's log on the server never shows a client secret, the signing key, a password, its hash or a token it hands
// out, not even the wrong secret of a rejected client or the wrong password of a rejected login.
public sealed class LoggingTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    [Fact]
    public async Task TokenRequests_LogNoSecret()
    {
        var signingKey = IdentityFactory.NewSigningKey();
        var wrongSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var identity = new IdentityFactory(new Dictionary<string, string?>
        {
            [WebHostDefaults.EnvironmentKey] = Environments.Production,
            ["Tokens:SigningKey"] = signingKey,
        });

        var token = await identity.GetTokenAsync();
        using var rejected = await identity.RequestTokenAsync(clientSecret: wrongSecret);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(identity.Logs.Containing(identity.ClientSecret, wrongSecret, signingKey, token));
    }

    // Setting up the account, a login, a wrong password and a throttled one, as on the server.
    [Fact]
    public async Task AccountsAndLogins_LogNoPassword()
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var newPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var wrongPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        await using var identity = new IdentityFactory(
            new Dictionary<string, string?> { [WebHostDefaults.EnvironmentKey] = Environments.Production },
            connectionString: database.ConnectionString);

        await identity.SetAccountAsync("logged", password);
        await identity.SetAccountAsync("logged", newPassword);
        using var login = await identity.LoginAsync("logged", newPassword);
        var body = await login.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestContext.Current.CancellationToken);
        var token = body!["access_token"].ToString()!;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var wrong = await identity.LoginAsync("logged", wrongPassword);
        }

        using var scope = identity.Services.CreateScope();
        var hash = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Accounts
            .Where(account => account.Name == "logged")
            .Select(account => account.PasswordHash)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotEmpty(identity.Logs.Entries);
        Assert.Empty(identity.Logs.Containing(password, newPassword, wrongPassword, hash, hash.Split('$')[^1], token, identity.ClientSecret));
    }
}