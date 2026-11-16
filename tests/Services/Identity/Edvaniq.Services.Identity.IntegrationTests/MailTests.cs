using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Services.Identity.Infrastructure;
using Edvaniq.Services.Identity.Infrastructure.Mail;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.IntegrationTests;

// #248: a system mail waits in the database until the mail service takes it. An outage loses none, the mail is tried
// again later, and after 24 hours it goes, sent or not, like with its account. Tests send nothing for real
// (RecordingMailSender), and the log names no address and no text.
public sealed class MailTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mail_IsSent_ThenGone()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var account = await UserAsync(identity);
        await QueueAsync(identity, account, mails => mails.Confirmation(account, "token-1"));

        await Dispatcher(identity).SendDueAsync(Cancel);

        var sent = Assert.Single(Sender(identity).Sent, mail => mail.To == account.Email);
        Assert.Equal("Bestätige deine E-Mail-Adresse", sent.Subject);
        Assert.Contains(IdentityFactory.PublicBaseUrl + "konto/bestaetigen#t=token-1", sent.Body, StringComparison.Ordinal);
        Assert.Empty(Mails(identity, account));
    }

    [Fact]
    public async Task Outage_KeepsTheMail_AndSendsItLater()
    {
        var time = new TestTime();
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString, time: time);
        var account = await UserAsync(identity);
        await QueueAsync(identity, account, mails => mails.Confirmation(account, "token-2"));
        Sender(identity).Outage = true;

        await Dispatcher(identity).SendDueAsync(Cancel);
        var waiting = Assert.Single(Mails(identity, account));
        Assert.Equal(1, waiting.Attempts);
        Assert.Equal(time.GetUtcNow().UtcDateTime + OutgoingMail.RetryDelays[0], waiting.DueAt, TimeSpan.FromMilliseconds(1));
        await Dispatcher(identity).SendDueAsync(Cancel);

        Sender(identity).Outage = false;
        time.Advance(OutgoingMail.RetryDelays[0]);
        await Dispatcher(identity).SendDueAsync(Cancel);
        Assert.Single(Sender(identity).Sent, mail => mail.To == account.Email);
        Assert.Empty(Mails(identity, account));

        var lines = string.Join('\n', identity.Logs.Entries.Select(entry => entry.Message));
        Assert.Contains("failed 1 times (IOException)", lines, StringComparison.Ordinal);
        Assert.DoesNotContain(account.Email!, lines, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-2", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("Bestätige", lines, StringComparison.Ordinal);
    }

    // The link in it has run out by then: the mail goes unsent, and a user who waits that long hears it (Delayed).
    [Fact]
    public async Task MailThatWaits_TellsItIsDelayed_AndGoesAfterADay()
    {
        var time = new TestTime();
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString, time: time);
        var account = await UserAsync(identity);
        await QueueAsync(identity, account, mails => mails.Confirmation(account, "token-3"));
        Sender(identity).Outage = true;

        time.Advance(SystemMails.DelayedAfter);
        Assert.True(await DelayedAsync(identity));
        time.Advance(OutgoingMail.MaxAge);
        await Dispatcher(identity).SendDueAsync(Cancel);

        Assert.Empty(Mails(identity, account));
        Assert.DoesNotContain(Sender(identity).Sent, mail => mail.To == account.Email);
    }

    // Without a mail service (#247) the mails wait, and the log says how many, once.
    [Fact]
    public async Task WithoutMailService_MailsWait()
    {
        await using var identity = new IdentityFactory(
            new Dictionary<string, string?> { ["Mail:Provider"] = "None" }, connectionString: database.ConnectionString);
        var account = await UserAsync(identity);
        await QueueAsync(identity, account, mails => mails.AlreadyRegistered(account));

        await Dispatcher(identity).SendDueAsync(Cancel);
        await Dispatcher(identity).SendDueAsync(Cancel);

        Assert.Single(Mails(identity, account));
        Assert.Null(identity.Services.GetService<RecordingMailSender>());
        Assert.Single(identity.Logs.Entries, entry => entry.Message.StartsWith("No mail service is set up", StringComparison.Ordinal));
    }

    // A deleted account takes its mails along, whoever deletes it.
    [Fact]
    public async Task DeletedAccount_TakesItsMails()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var account = await UserAsync(identity);
        await QueueAsync(identity, account, mails => mails.Confirmation(account, "token-4"));

        await StoreAsync(identity, db => db.Accounts.Remove(db.Accounts.Single(item => item.Id == account.Id)));

        Assert.Empty(Mails(identity, account));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("app.example.org")]
    [InlineData("http://app.example.org/")]
    [InlineData("https://app.example.org/?a=1")]
    public void WrongPublicBaseUrl_StopsTheStart(string? url)
    {
        using var identity = new IdentityFactory(
            new Dictionary<string, string?> { ["Mail:PublicBaseUrl"] = url }, connectionString: database.ConnectionString);

        var failure = StartFailure.Of<OptionsValidationException>(() => identity.CreateClient());

        Assert.Contains("Mail:PublicBaseUrl", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoopbackOverHttp_IsFine_ForLocalRuns()
    {
        await using var identity = new IdentityFactory(
            new Dictionary<string, string?> { ["Mail:PublicBaseUrl"] = "http://localhost:5096" }, connectionString: database.ConnectionString);

        Assert.Equal("http://localhost:5096/login", identity.Services.GetRequiredService<MailLinks>().Login);
    }

    // A user account as registration makes it, straight into the database until #249 is there.
    private static async Task<Account> UserAsync(IdentityFactory identity)
    {
        var account = Account.CreateUser($"user-{Guid.NewGuid():N}@example.org", "Lernfuchs", 2005, "hash", DateTime.UtcNow);
        await StoreAsync(identity, db => db.Accounts.Add(account));
        return account;
    }

    private static async Task QueueAsync(IdentityFactory identity, Account account, Action<SystemMails> write)
    {
        using var scope = identity.Services.CreateScope();
        write(scope.ServiceProvider.GetRequiredService<SystemMails>());
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SaveChangesAsync(Cancel);
    }

    private static async Task<bool> DelayedAsync(IdentityFactory identity)
    {
        using var scope = identity.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<SystemMails>().DelayedAsync(Cancel);
    }

    private static MailDispatcher Dispatcher(IdentityFactory identity) => identity.Services.GetRequiredService<MailDispatcher>();

    private static RecordingMailSender Sender(IdentityFactory identity) => identity.Services.GetRequiredService<RecordingMailSender>();

    private static List<OutgoingMail> Mails(IdentityFactory identity, Account account) =>
        Query(identity, db => db.OutgoingMails.AsNoTracking().Where(mail => mail.AccountId == account.Id).ToList());

    private static async Task StoreAsync(IdentityFactory identity, Action<IdentityDbContext> change)
    {
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        change(db);
        await db.SaveChangesAsync(Cancel);
    }

    private static T Query<T>(IdentityFactory identity, Func<IdentityDbContext, T> query)
    {
        using var scope = identity.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }
}