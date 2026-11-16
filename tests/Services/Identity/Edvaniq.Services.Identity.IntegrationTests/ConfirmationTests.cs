using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.Services.Identity.Api.Accounts;
using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Services.Identity.Infrastructure;
using Edvaniq.Services.Identity.Infrastructure.Mail;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// #250: a new account gets a mail with a link, the link confirms the address once within 24 hours, and only then the
// account can log in. Whoever might be a minor by the year of birth waits for the operator's approval (#256). A new
// link replaces the old one, with a limit per account, and asking never tells whether the address has an account.
// #266 in part: an account not confirmed within 7 days goes, with its mails and links.
public sealed partial class ConfirmationTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private const string Password = "ein langes Passwort";

    private const string Legal = "vorlaeufig-1";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Link_ConfirmsTheAddress_ThenTheAccountLogsIn_AndTheLinkIsUsed()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = await RegisterAsync(identity, birthYear: 2000);
        var token = Assert.Single(await SentTokensAsync(identity, email));

        using var confirmed = await ConfirmAsync(identity, token);
        using var again = await ConfirmAsync(identity, token);
        using var login = await identity.LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        await AssertRefusedAsync(again, "token_used");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var account = Find(identity, email)!;
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.NotNull(account.ConfirmedAt);
    }

    // Of several uses of one link at once, exactly one confirms.
    [Fact]
    public async Task OneLink_UsedAtTheSameTime_ConfirmsOnce()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = await RegisterAsync(identity, birthYear: 2000);
        var token = Assert.Single(await SentTokensAsync(identity, email));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ConfirmAsync(identity, token)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Equal(4, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // A minor by the year of birth: the account waits for the operator and can't log in yet.
    [Fact]
    public async Task Minor_WaitsForApproval_AfterConfirming()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = await RegisterAsync(identity, birthYear: DateTime.UtcNow.Year - AccountProfile.AgeOfMajority + 1);

        using var confirmed = await ConfirmAsync(identity, Assert.Single(await SentTokensAsync(identity, email)));
        using var login = await identity.LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Equal(AccountStatus.AwaitingApproval, Find(identity, email)!.Status);
        Assert.Equal("awaiting_approval", (await login.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ExpiredLink_IsRefused_AndTheAccountStaysUnconfirmed()
    {
        var time = new TestTime();
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString, time: time);
        var email = await RegisterAsync(identity);
        var token = Assert.Single(await SentTokensAsync(identity, email));
        time.Advance(AccountToken.LifetimeOf(TokenPurpose.Confirmation));

        using var response = await ConfirmAsync(identity, token);

        await AssertRefusedAsync(response, "token_expired");
        Assert.Equal(AccountStatus.Unconfirmed, Find(identity, email)!.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task UnknownLink_IsRefused(string? token)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        using var response = await ConfirmAsync(identity, token);

        await AssertRefusedAsync(response, "token_invalid");
    }

    // A new link replaces the old one. At most one a minute and five a day per account, the first one included, and a
    // mail still waiting is not doubled.
    [Fact]
    public async Task NewLink_ReplacesTheOld_WithinItsLimits()
    {
        var time = new TestTime();
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString, time: time);
        var email = await RegisterAsync(identity);
        await ResendAsync(identity, email);
        Assert.Single(Waiting(identity, email));
        var first = Assert.Single(await SentTokensAsync(identity, email));

        await ResendAsync(identity, email);
        Assert.Empty(Waiting(identity, email));
        time.Advance(Confirmation.ResendPause);
        await ResendAsync(identity, email.ToUpperInvariant());
        await ResendAsync(identity, email);
        Assert.Single(Waiting(identity, email));
        Assert.Equal(2, (await SentTokensAsync(identity, email)).Count);

        using (var old = await ConfirmAsync(identity, first))
        {
            await AssertRefusedAsync(old, "token_used");
        }

        for (var i = 0; i < Confirmation.ResendsPerDay; i++)
        {
            time.Advance(Confirmation.ResendPause);
            await ResendAsync(identity, email);
            await SentTokensAsync(identity, email);
        }

        var tokens = await SentTokensAsync(identity, email);
        Assert.Equal(Confirmation.ResendsPerDay, tokens.Count);
        using var newest = await ConfirmAsync(identity, tokens[^1]);
        Assert.Equal(HttpStatusCode.NoContent, newest.StatusCode);
    }

    // Many requests at once count like one after the other: one new mail at most.
    [Fact]
    public async Task ManyRequestsAtOnce_QueueOneMail()
    {
        var time = new TestTime();
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString, time: time);
        var email = await RegisterAsync(identity);
        await SentTokensAsync(identity, email);
        time.Advance(Confirmation.ResendPause);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => ResendAsync(identity, email)));

        Assert.Single(Waiting(identity, email));
    }

    // The same answer whether the address is unknown, confirmed or not: nobody learns who has an account.
    [Fact]
    public async Task Resend_AnswersTheSame_ForAnyAddress()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var confirmed = await RegisterAsync(identity, birthYear: 2000);
        using (await ConfirmAsync(identity, Assert.Single(await SentTokensAsync(identity, confirmed))))
        {
        }

        var unknown = await ResendAsync(identity, $"nobody-{Guid.NewGuid():N}@example.org");
        var known = await ResendAsync(identity, confirmed);
        var invalid = await ResendAsync(identity, "kein-at");

        Assert.Equal(unknown, known);
        Assert.Equal(unknown, invalid);
        Assert.Empty(Waiting(identity, confirmed));
    }

    // While no mail service is set up, the operator confirms by hand, with the same age rule.
    [Fact]
    public async Task Operator_ConfirmsByHand()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var adult = await RegisterAsync(identity, birthYear: 2000);
        var minor = await RegisterAsync(identity, birthYear: DateTime.UtcNow.Year - 10);

        Assert.Equal("confirmed", await CommandAsync(identity, adult));
        Assert.Equal("no account", await CommandAsync(identity, adult));
        Assert.Equal("confirmed", await CommandAsync(identity, minor));
        Assert.Equal("no account", await CommandAsync(identity, "nobody@example.org"));

        Assert.Equal((AccountStatus.Active, AccountStatus.AwaitingApproval), (Find(identity, adult)!.Status, Find(identity, minor)!.Status));
    }

    // A new registration takes over an unconfirmed address, the old account's mails and links go with it.
    [Fact]
    public async Task NewRegistration_ReplacesTheUnconfirmedAccount_WithItsLinks()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = await RegisterAsync(identity);
        var oldToken = Assert.Single(await SentTokensAsync(identity, email));
        var old = Find(identity, email)!.Id;

        await RegisterAsync(identity, email: email);

        Assert.NotEqual(old, Find(identity, email)!.Id);
        Assert.False(Query(identity, db => db.AccountTokens.Any(token => token.AccountId == old)));
        using var response = await ConfirmAsync(identity, oldToken);
        await AssertRefusedAsync(response, "token_invalid");
    }

    // Not confirmed within 7 days, the account goes with its mails and links. A confirmed one stays, its expired link
    // goes a week after it ran out. Only counts get into the log.
    [Fact]
    public async Task Sweep_DeletesUnconfirmedAccountsAfterAWeek_AndOldLinks()
    {
        var time = new TestTime();
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString, time: time);
        var unconfirmed = await RegisterAsync(identity);
        await SentTokensAsync(identity, unconfirmed);
        var confirmed = await RegisterAsync(identity, birthYear: 2000);
        using (await ConfirmAsync(identity, Assert.Single(await SentTokensAsync(identity, confirmed))))
        {
        }

        var id = Find(identity, unconfirmed)!.Id;
        var sweep = identity.Services.GetRequiredService<AccountSweep>();
        time.Advance(AccountSweep.UnconfirmedFor - TimeSpan.FromMinutes(1));
        sweep.Sweep();
        Assert.NotNull(Find(identity, unconfirmed));

        time.Advance(TimeSpan.FromMinutes(1));
        sweep.Sweep();
        time.Advance(AccountToken.LifetimeOf(TokenPurpose.Confirmation) + AccountSweep.ExpiredFor);
        sweep.Sweep();

        Assert.Null(Find(identity, unconfirmed));
        Assert.False(Query(identity, db => db.AccountTokens.Any(token => token.AccountId == id)));
        Assert.False(Query(identity, db => db.OutgoingMails.Any(mail => mail.AccountId == id)));
        Assert.Equal(AccountStatus.Active, Find(identity, confirmed)!.Status);
        var confirmedId = Find(identity, confirmed)!.Id;
        Assert.False(Query(identity, db => db.AccountTokens.Any(token => token.AccountId == confirmedId)));
        var lines = string.Join('\n', identity.Logs.Entries.Select(entry => entry.Text));
        Assert.Contains("accounts not confirmed within", lines, StringComparison.Ordinal);
        Assert.DoesNotContain(unconfirmed, lines, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> RegisterAsync(IdentityFactory identity, int birthYear = 2005, string? email = null)
    {
        string code;
        using (var scope = identity.Services.CreateScope())
        {
            code = (await scope.ServiceProvider.GetRequiredService<InviteService>().CreateAsync(1, Cancel))[0].Code;
        }

        email ??= $"lena-{Guid.NewGuid():N}@example.org";
        using var response = await identity.CreateClient().PostAsJsonAsync(
            "/accounts",
            new { email, password = Password, inviteCode = code, pseudonym = "Lernfuchs", birthYear, acceptedTermsVersion = Legal, acceptedPrivacyVersion = Legal },
            Cancel);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return email;
    }

    private static Task<HttpResponseMessage> ConfirmAsync(IdentityFactory identity, string? token) =>
        identity.CreateClient().PostAsJsonAsync("/accounts/confirmations", new { token }, Cancel);

    private static async Task<string> ResendAsync(IdentityFactory identity, string email)
    {
        using var response = await identity.CreateClient().PostAsJsonAsync("/accounts/confirmations/resend", new { email }, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancel);
    }

    private static async Task<string> CommandAsync(IdentityFactory identity, string email)
    {
        using var output = new StringWriter();
        Assert.Equal(AccountCommand.Ok, await AccountCommand.RunAsync(identity.Services, ["account", "confirm", email], TextReader.Null, output, TextWriter.Null));
        return output.ToString().Trim();
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string refusal)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(refusal, (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
    }

    // Sends what is due, then the tokens of every confirmation link the address got so far, oldest first.
    private static async Task<List<string>> SentTokensAsync(IdentityFactory identity, string email)
    {
        await identity.Services.GetRequiredService<MailDispatcher>().SendDueAsync(Cancel);
        return [.. identity.Services.GetRequiredService<RecordingMailSender>().Sent
            .Where(mail => mail.To == email.ToLowerInvariant() && mail.Subject == "Bestätige deine E-Mail-Adresse")
            .Select(mail => Uri.UnescapeDataString(Token().Match(mail.Body).Groups[1].Value))];
    }

    private static List<OutgoingMail> Waiting(IdentityFactory identity, string email)
    {
        var id = Find(identity, email)!.Id;
        return Query(identity, db => db.OutgoingMails.AsNoTracking().Where(mail => mail.AccountId == id).ToList());
    }

    private static Account? Find(IdentityFactory identity, string email) =>
        Query(identity, db => db.Accounts.AsNoTracking().SingleOrDefault(account => account.Email == email.ToLowerInvariant()));

    private static T Query<T>(IdentityFactory identity, Func<IdentityDbContext, T> query)
    {
        using var scope = identity.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }

    [GeneratedRegex(@"#t=(\S+)")]
    private static partial Regex Token();
}