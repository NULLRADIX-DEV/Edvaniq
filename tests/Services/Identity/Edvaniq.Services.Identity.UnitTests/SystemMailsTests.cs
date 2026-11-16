using System.Text.RegularExpressions;

using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Testing;

namespace Edvaniq.Services.Identity.UnitTests;

// #248: the system mails are short German plain text with links only to the web app, nothing that tracks and nothing a
// user wrote. The outbox holds only that a mail is due, the text comes when it is sent.
public sealed partial class SystemMailsTests
{
    private const string Base = "https://app.example.org/";

    private static readonly MailLinks Links = new(new Uri(Base));

    private readonly Outbox outbox = new();

    private readonly TestTime time = new();

    private readonly Account account = Account.CreateUser("lena@example.org", "paypal-login.example", 2009, "hash", DateTime.UtcNow);

    private SystemMails Mails => new(outbox, time);

    [Fact]
    public void AskingForAMail_QueuesItsKindOnly()
    {
        Mails.Confirmation(account);

        var mail = Assert.Single(outbox.Mails);
        Assert.Equal((MailKind.Confirmation, account.Id, time.GetUtcNow().UtcDateTime, 0), (mail.Kind, mail.AccountId, mail.DueAt, mail.Attempts));
    }

    [Fact]
    public void Confirmation_LinksToTheConfirmPage_WithTheTokenInTheFragment()
    {
        var mail = SystemMails.Write(MailKind.Confirmation, Links, "abc-DEF_123");

        Assert.Equal([Base + "konto/bestaetigen#t=abc-DEF_123"], LinksIn(mail.Body));
        Assert.Contains("24 Stunden", mail.Body, StringComparison.Ordinal);
        Assert.Contains("nach 7 Tagen gelöscht", mail.Body, StringComparison.Ordinal);
        Assert.Equal(TokenPurpose.Confirmation, SystemMails.TokenOf(MailKind.Confirmation));
    }

    [Fact]
    public void PasswordReset_LinksToTheResetPage_AndSaysItHoldsAnHour()
    {
        var mail = SystemMails.Write(MailKind.PasswordReset, Links, "tok");

        Assert.Equal([Base + "konto/passwort-neu#t=tok"], LinksIn(mail.Body));
        Assert.Contains("eine Stunde", mail.Body, StringComparison.Ordinal);
        Assert.Equal(TokenPurpose.PasswordReset, SystemMails.TokenOf(MailKind.PasswordReset));
    }

    [Fact]
    public void AlreadyRegistered_LinksToTheLogin_WithoutToken()
    {
        var mail = SystemMails.Write(MailKind.AlreadyRegistered, Links, null);

        Assert.Equal([Base + "login"], LinksIn(mail.Body));
        Assert.Null(SystemMails.TokenOf(MailKind.AlreadyRegistered));
    }

    // No HTML, no images, no tracking parameters, no pseudonym, a few sentences.
    [Theory]
    [InlineData(MailKind.Confirmation)]
    [InlineData(MailKind.AlreadyRegistered)]
    [InlineData(MailKind.PasswordReset)]
    public void EveryMail_IsShortPlainTextWithoutTracking(MailKind kind)
    {
        var mail = SystemMails.Write(kind, Links, "t1");

        Assert.StartsWith("Hallo,\n", mail.Body, StringComparison.Ordinal);
        Assert.EndsWith("\n\nDein Edvaniq-Team", mail.Body, StringComparison.Ordinal);
        Assert.DoesNotMatch("<[a-z/!]", mail.Body);
        Assert.DoesNotContain("utm_", mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(account.Pseudonym!, mail.Body, StringComparison.Ordinal);
        Assert.All(LinksIn(mail.Body), link => Assert.StartsWith(Base, link, StringComparison.Ordinal));
        Assert.InRange(WordsOf(mail.Body), 20, 90);
        Assert.InRange(mail.Subject.Length, 10, 60);
        Assert.DoesNotContain("\r", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamAccount_GetsNoMail() =>
        Assert.Throws<InvalidOperationException>(() => Mails.Confirmation(Account.Create("ben", "hash", DateTime.UtcNow)));

    // After a failure the waits grow, then stay at the last one.
    [Fact]
    public void FailedMail_WaitsLongerEachTime()
    {
        Mails.Confirmation(account);
        var mail = outbox.Mails[0];
        var now = time.GetUtcNow().UtcDateTime;

        var waits = Enumerable.Range(0, 6).Select(_ =>
        {
            mail.Postpone(now);
            return mail.DueAt - now;
        }).ToList();

        Assert.Equal(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(1), TimeSpan.FromHours(1)],
            waits);
        Assert.Equal(6, mail.Attempts);
        Assert.Equal(mail.CreatedAt + OutgoingMail.MaxAge, mail.ExpiresAt);
    }

    [Fact]
    public async Task Delayed_WhenAMailWaitsLongerThanFiveMinutes()
    {
        Assert.False(await Mails.DelayedAsync(TestContext.Current.CancellationToken));
        Assert.Equal(time.GetUtcNow().UtcDateTime - SystemMails.DelayedAfter, outbox.AskedSince);
    }

    // A link token is 256 random bits, URL-safe, and only its hash is kept.
    [Fact]
    public void Token_IsRandom_AndKeptAsHash()
    {
        var (first, value) = AccountToken.Create(account, TokenPurpose.Confirmation, DateTime.UtcNow);
        var (_, other) = AccountToken.Create(account, TokenPurpose.Confirmation, DateTime.UtcNow);

        Assert.Matches("^[A-Za-z0-9_-]{43}$", value);
        Assert.NotEqual(value, other);
        Assert.Equal(AccountToken.HashOf(value), first.Hash);
        Assert.Equal(first.CreatedAt + TimeSpan.FromHours(24), first.ExpiresAt);
    }

    private static List<string> LinksIn(string body) => [.. Link().Matches(body).Select(match => match.Value)];

    private static int WordsOf(string body) => body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Link();

    private sealed class Outbox : IMailOutbox
    {
        public List<OutgoingMail> Mails { get; } = [];

        public DateTime? AskedSince { get; private set; }

        public void Add(OutgoingMail mail) => Mails.Add(mail);

        public Task<bool> AnyWaitingSinceAsync(DateTime before, CancellationToken cancellationToken)
        {
            AskedSince = before;
            return Task.FromResult(false);
        }
    }
}