using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Identity.Api.Accounts;
using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Services.Identity.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// #249, #255: registering takes an open invitation code, once, and checks everything before the code is gone. The
// answer never tells whether the address has an account, and the new account can't log in before its address is
// confirmed. Pseudonym and year of birth are kept, nothing else about the person.
public sealed class RegistrationTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private const string Password = "ein langes Passwort";

    // The versions of the legal texts in appsettings.json until #260.
    private const string Legal = "vorlaeufig-1";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValidRegistration_MakesAnUnconfirmedAccount_ThatCannotLogInYet()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var code = await CodeAsync(identity);
        var email = NewEmail();

        using var response = await RegisterAsync(identity, email, code, pseudonym: "  Lern  Fuchs ", birthYear: 2010);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("mailDelayed").GetBoolean());
        var account = Single(identity, email);
        Assert.Equal((AccountKind.User, AccountStatus.Unconfirmed, "Lern Fuchs", (int?)2010), (account.Kind, account.Status, account.Pseudonym, account.BirthYear));
        Assert.Null(account.Name);
        Assert.Null(account.ConfirmedAt);
        Assert.StartsWith("pbkdf2-sha256$", account.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(InviteStatus.Redeemed, (await InvitesAsync(identity)).Single(invite => invite.CodeHash == InviteCode.HashOf(InviteCode.Normalize(code)!)).Status);

        using var login = await identity.LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal("unconfirmed", (await login.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(null, "invite_missing")]
    [InlineData("", "invite_missing")]
    [InlineData("nicht-gueltig", "invite_invalid")]
    [InlineData("AAAA-AAAA-AAAA", "invite_invalid")]
    public async Task MissingOrUnknownCode_IsRefused(string? code, string refusal)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = NewEmail();

        using var response = await RegisterAsync(identity, email, code);

        await AssertRefusedAsync(response, refusal);
        Assert.Null(Find(identity, email));
    }

    [Fact]
    public async Task RedeemedOrRevokedCode_IsRefused()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var used = await CodeAsync(identity);
        using (await RegisterAsync(identity, NewEmail(), used))
        {
        }

        var revoked = await CodeAsync(identity);
        using (var scope = identity.Services.CreateScope())
        {
            var invites = scope.ServiceProvider.GetRequiredService<InviteService>();
            var label = (await invites.ListAsync(Cancel)).Single(invite => invite.CodeHash == InviteCode.HashOf(InviteCode.Normalize(revoked)!)).Label;
            Assert.Equal(RevokeResult.Revoked, await invites.RevokeAsync(label, Cancel));
        }

        using var again = await RegisterAsync(identity, NewEmail(), used);
        using var withRevoked = await RegisterAsync(identity, NewEmail(), revoked);

        await AssertRefusedAsync(again, "invite_redeemed");
        await AssertRefusedAsync(withRevoked, "invite_revoked");
    }

    // A typo in anything else costs no code: it is still open afterwards.
    [Theory]
    [InlineData("kein-at", Password, "Lernfuchs", 2010, "email_invalid")]
    [InlineData(null, "zu kurz", "Lernfuchs", 2010, "password_rules")]
    [InlineData(null, Password, "ab", 2010, "pseudonym_invalid")]
    [InlineData(null, Password, "name@schule.de", 2010, "pseudonym_invalid")]
    [InlineData(null, Password, "Lernfuchs", 1900, "birth_year_invalid")]
    [InlineData(null, Password, "Lernfuchs", 2024, "birth_year_invalid")]
    [InlineData(null, Password, "Lernfuchs", null, "birth_year_invalid")]
    public async Task InvalidInput_IsRefused_AndKeepsTheCode(string? email, string password, string pseudonym, int? birthYear, string refusal)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var code = await CodeAsync(identity);

        using var response = await RegisterAsync(identity, email ?? NewEmail(), code, password, pseudonym, birthYear);

        await AssertRefusedAsync(response, refusal);
        using var later = await RegisterAsync(identity, NewEmail(), code);
        Assert.Equal(HttpStatusCode.Accepted, later.StatusCode);
    }

    // The same answer as for a new address, the code is gone all the same, and the address hears it has an account.
    [Fact]
    public async Task KnownAddress_GetsTheSameAnswer_AndAMail()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = NewEmail();
        using (await RegisterAsync(identity, email, await CodeAsync(identity)))
        {
        }

        await ActivateAsync(identity, email);
        var code = await CodeAsync(identity);
        using var response = await RegisterAsync(identity, email.ToUpperInvariant(), code, "anderes Passwort!!", "Jemand Anderes", 2000);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("mailDelayed").GetBoolean());
        var account = Single(identity, email);
        Assert.Equal(("Lernfuchs", (int?)2010), (account.Pseudonym, account.BirthYear));
        Assert.Contains(Query(identity, db => db.OutgoingMails.Where(mail => mail.AccountId == account.Id).ToList()), mail => mail.Kind == MailKind.AlreadyRegistered);
        using var again = await RegisterAsync(identity, NewEmail(), code);
        await AssertRefusedAsync(again, "invite_redeemed");
    }

    // An unconfirmed account doesn't hold the address: registering it again replaces the account, its mails go with it.
    [Fact]
    public async Task UnconfirmedAddress_IsTakenOverByANewRegistration()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = NewEmail();
        using (await RegisterAsync(identity, email, await CodeAsync(identity), pseudonym: "Erster"))
        {
        }

        var first = Single(identity, email);
        using var response = await RegisterAsync(identity, email, await CodeAsync(identity), pseudonym: "Zweiter", birthYear: 2001);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var account = Single(identity, email);
        Assert.NotEqual(first.Id, account.Id);
        Assert.Equal(("Zweiter", (int?)2001, AccountStatus.Unconfirmed), (account.Pseudonym, account.BirthYear, account.Status));
        Assert.False(Query(identity, db => db.OutgoingMails.Any(mail => mail.AccountId == first.Id)));
    }

    // Of several registrations with one code at once, exactly one gets it.
    [Fact]
    public async Task OneCode_AtTheSameTime_MakesOneAccount()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var code = await CodeAsync(identity);
        var emails = Enumerable.Range(0, 5).Select(_ => NewEmail()).ToList();

        var responses = await Task.WhenAll(emails.Select(email => RegisterAsync(identity, email, code)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(4, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        Assert.Single(emails, email => Find(identity, email) is not null);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // Address, pseudonym and password stay out of the log.
    [Fact]
    public async Task Registration_LogsNothingOfThePerson()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = NewEmail();

        using (await RegisterAsync(identity, email, await CodeAsync(identity), pseudonym: "Geheimfuchs"))
        {
        }

        var lines = string.Join('\n', identity.Logs.Entries.Select(entry => entry.Text));
        Assert.DoesNotContain(email, lines, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Geheimfuchs", lines, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, lines, StringComparison.Ordinal);
    }

    // #261: without agreeing to the current versions of the legal texts there is no account, and the code stays open.
    [Theory]
    [InlineData(null, Legal, "consent_missing")]
    [InlineData(Legal, "", "consent_missing")]
    [InlineData("vorlaeufig-0", Legal, "consent_outdated")]
    [InlineData(Legal, "vorlaeufig-0", "consent_outdated")]
    public async Task WithoutCurrentConsent_IsRefused_AndKeepsTheCode(string? terms, string? privacy, string refusal)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var code = await CodeAsync(identity);
        var email = NewEmail();

        using var response = await RegisterAsync(identity, email, code, terms: terms, privacy: privacy);

        await AssertRefusedAsync(response, refusal);
        Assert.Null(Find(identity, email));
        using var later = await RegisterAsync(identity, NewEmail(), code);
        Assert.Equal(HttpStatusCode.Accepted, later.StatusCode);
    }

    // The versions agreed to and the time are kept at the account, the operator reads them with account consent.
    [Fact]
    public async Task Consent_IsKept_AndTheOperatorCanReadIt()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = NewEmail();

        using (await RegisterAsync(identity, email, await CodeAsync(identity)))
        {
        }

        var account = Single(identity, email);
        Assert.Equal((Legal, Legal), (account.TermsVersion, account.PrivacyVersion));
        Assert.NotNull(account.ConsentedAt);
        using var output = new StringWriter();
        Assert.Equal(AccountCommand.Ok, await AccountCommand.RunAsync(identity.Services, ["account", "consent", email], TextReader.Null, output, TextWriter.Null));
        Assert.StartsWith($"terms {Legal} privacy {Legal} at ", output.ToString(), StringComparison.Ordinal);
    }

    // The client asks which versions to show for agreeing, instead of knowing them itself.
    [Fact]
    public async Task LegalVersions_AreOpenToAnyone()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        using var response = await identity.CreateClient().GetAsync("/legal-versions", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal((Legal, Legal), (body.GetProperty("termsVersion").GetString(), body.GetProperty("privacyVersion").GetString()));
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
    }

    private static Task<HttpResponseMessage> RegisterAsync(
        IdentityFactory identity,
        string email,
        string? code,
        string password = Password,
        string pseudonym = "Lernfuchs",
        int? birthYear = 2010,
        string? terms = Legal,
        string? privacy = Legal) =>
        identity.CreateClient().PostAsJsonAsync(
            "/accounts",
            new { email, password, inviteCode = code, pseudonym, birthYear, acceptedTermsVersion = terms, acceptedPrivacyVersion = privacy },
            Cancel);

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string refusal)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(refusal, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
    }

    private static async Task<string> CodeAsync(IdentityFactory identity)
    {
        using var scope = identity.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<InviteService>().CreateAsync(1, Cancel))[0].Code;
    }

    private static async Task<IReadOnlyList<InviteCode>> InvitesAsync(IdentityFactory identity)
    {
        using var scope = identity.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<InviteService>().ListAsync(Cancel);
    }

    // As a confirmed account, without the link (#250).
    private static async Task ActivateAsync(IdentityFactory identity, string email)
    {
        using var scope = identity.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Accounts
            .Where(account => account.Email == email)
            .ExecuteUpdateAsync(set => set.SetProperty(account => account.Status, AccountStatus.Active), Cancel);
    }

    private static string NewEmail() => $"lena-{Guid.NewGuid():N}@example.org";

    private static Account? Find(IdentityFactory identity, string email) =>
        Query(identity, db => db.Accounts.AsNoTracking().SingleOrDefault(account => account.Email == email.ToLowerInvariant()));

    private static Account Single(IdentityFactory identity, string email) => Find(identity, email) ?? throw new InvalidOperationException("No account.");

    private static T Query<T>(IdentityFactory identity, Func<IdentityDbContext, T> query)
    {
        using var scope = identity.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }
}