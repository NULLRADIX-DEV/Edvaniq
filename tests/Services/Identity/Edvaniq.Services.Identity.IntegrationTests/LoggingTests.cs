using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Infrastructure;
using Edvaniq.Services.Identity.Infrastructure.Mail;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Identity.IntegrationTests;

// Identity's log on the server never shows a client secret, the signing key, a password, its hash or a token it hands
// out, not even the wrong secret of a rejected client or the wrong password of a rejected login.
public sealed partial class LoggingTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    [Fact]
    public async Task TokenRequests_LogNoSecret()
    {
        var signingKey = IdentityFactory.NewSigningKey();
        var previousKey = IdentityFactory.NewSigningKey();
        var wrongSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var identity = new IdentityFactory(new Dictionary<string, string?>
        {
            [WebHostDefaults.EnvironmentKey] = Environments.Production,
            ["Tokens:SigningKey"] = signingKey,
            ["Tokens:PreviousSigningKey"] = previousKey,
        });

        var token = await identity.GetTokenAsync();
        using var rejected = await identity.RequestTokenAsync(clientSecret: wrongSecret);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(identity.Logs.Containing(identity.ClientSecret, wrongSecret, signingKey, previousKey, token));
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

        // #244: an address is no secret, but it names a person, so it stays out of the log as well, also when throttled.
        var email = $"logged-{Guid.NewGuid():N}@example.org";
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var byAddress = await identity.LoginAsync(email, wrongPassword);
        }

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotEmpty(identity.Logs.Entries);
        Assert.Empty(identity.Logs.Containing(email, email.Split('@')[0]));
        Assert.Empty(identity.Logs.Containing(password, newPassword, wrongPassword, hash, hash.Split('$')[^1], token, identity.ClientSecret));
    }

    // #314: the way of a new person, from the invite code over the mail with the link to the first login by address.
    // Nothing they typed or got gets into the log: address, password, pseudonym, year of birth, code, link and token.
    [Fact]
    public async Task Registration_ToTheFirstLogin_LogsNothingOfThePerson()
    {
        var cancel = TestContext.Current.CancellationToken;
        var email = $"Fluss-{Guid.NewGuid():N}@example.org";
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var wrongPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var pseudonym = $"Fuchs{RandomNumberGenerator.GetHexString(8)}";
        const int birthYear = 1931;
        await using var identity = new IdentityFactory(
            new Dictionary<string, string?> { [WebHostDefaults.EnvironmentKey] = Environments.Production },
            connectionString: database.ConnectionString);
        string code;
        using (var scope = identity.Services.CreateScope())
        {
            code = (await scope.ServiceProvider.GetRequiredService<InviteService>().CreateAsync(1, cancel))[0].Code;
        }

        var client = identity.CreateClient();
        using var registered = await client.PostAsJsonAsync("/accounts", new { email, password, inviteCode = code, pseudonym, birthYear, acceptedTermsVersion = "vorlaeufig-1", acceptedPrivacyVersion = "vorlaeufig-1" }, cancel);
        await identity.Services.GetRequiredService<MailDispatcher>().SendDueAsync(cancel);
        var mail = Assert.Single(identity.Services.GetRequiredService<RecordingMailSender>().Sent, sent => sent.To == email.ToLowerInvariant());
        var link = Link().Match(mail.Body).Value;
        var confirmation = Uri.UnescapeDataString(link.Split("#t=")[1]);
        using var confirmed = await client.PostAsJsonAsync("/accounts/confirmations", new { token = confirmation }, cancel);
        using var resent = await client.PostAsJsonAsync("/accounts/confirmations/resend", new { email }, cancel);
        using var wrong = await identity.LoginAsync(email, wrongPassword);
        using var login = await identity.LoginAsync(email, password);
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancel))!["access_token"].ToString()!;

        Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotEmpty(identity.Logs.Entries);
        identity.Logs.AssertNone(email, email.Split('@')[0], password, wrongPassword, pseudonym, code, link, confirmation, token, identity.ClientSecret);
        Assert.DoesNotContain(identity.Logs.Entries, entry => BirthYear().IsMatch(entry.Text));
    }

    [GeneratedRegex(@"\S+#t=\S+")]
    private static partial Regex Link();

    // The year as a number of its own, not as part of an id or a duration.
    [GeneratedRegex(@"(?<![0-9A-Za-z.])1931(?![0-9A-Za-z.])")]
    private static partial Regex BirthYear();
}