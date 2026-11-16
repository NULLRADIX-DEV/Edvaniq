using System.Text.RegularExpressions;

using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Testing;

namespace Edvaniq.Services.Identity.UnitTests;

// #248: the system mails are short German plain text with links only to the web app, and nothing that tracks.
public sealed partial class SystemMailsTests
{
    private const string Base = "https://app.example.org/";

    private readonly Outbox outbox = new();

    private readonly TestTime time = new();

    private readonly Account account = Account.CreateUser("lena@example.org", "Lernfuchs", 2009, "hash", DateTime.UtcNow);

    private SystemMails Mails => new(outbox, new MailLinks(new Uri(Base)), time);

    [Fact]
    public void Confirmation_LinksToTheConfirmPage_WithTheTokenInTheFragment()
    {
        Mails.Confirmation(account, "abc-DEF_123");

        var mail = Assert.Single(outbox.Mails);
        Assert.Equal((MailKind.Confirmation, account.Id, "lena@example.org"), (mail.Kind, mail.AccountId, mail.To));
        Assert.Equal([Base + "konto/bestaetigen#t=abc-DEF_123"], Links(mail.Body));
        Assert.Contains("Hallo Lernfuchs,", mail.Body, StringComparison.Ordinal);
        Assert.Contains("24 Stunden", mail.Body, StringComparison.Ordinal);
        Assert.Equal(time.GetUtcNow().UtcDateTime, mail.DueAt);
    }

    [Fact]
    public void PasswordReset_LinksToTheResetPage_AndSaysItHoldsAnHour()
    {
        Mails.PasswordReset(account, "tok");

        var mail = Assert.Single(outbox.Mails);
        Assert.Equal([Base + "konto/passwort-neu#t=tok"], Links(mail.Body));
        Assert.Contains("eine Stunde", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AlreadyRegistered_LinksToTheLogin_AndNamesNoPseudonym()
    {
        Mails.AlreadyRegistered(account);

        var mail = Assert.Single(outbox.Mails);
        Assert.Equal([Base + "login"], Links(mail.Body));
        Assert.DoesNotContain("Lernfuchs", mail.Body, StringComparison.Ordinal);
    }

    // No HTML, no images, no tracking parameters, every line short enough for a phone, nothing beyond the link and a few
    // sentences.
    [Fact]
    public void EveryMail_IsShortPlainTextWithoutTracking()
    {
        Mails.Confirmation(account, "t1");
        Mails.AlreadyRegistered(account);
        Mails.PasswordReset(account, "t2");

        Assert.All(outbox.Mails, mail =>
        {
            Assert.DoesNotMatch("<[a-z/!]", mail.Body);
            Assert.DoesNotContain("utm_", mail.Body, StringComparison.OrdinalIgnoreCase);
            Assert.All(Links(mail.Body), link => Assert.StartsWith(Base, link, StringComparison.Ordinal));
            Assert.InRange(WordsOf(mail.Body), 20, 90);
            Assert.InRange(mail.Subject.Length, 10, OutgoingMail.SubjectMaxLength);
            Assert.True(mail.Body.Length <= OutgoingMail.BodyMaxLength);
            Assert.DoesNotContain("\r", mail.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TeamAccount_GetsNoMail() =>
        Assert.Throws<InvalidOperationException>(() => Mails.Confirmation(Account.Create("ben", "hash", DateTime.UtcNow), "t"));

    // After a failure the waits grow, then stay at the last one.
    [Fact]
    public void FailedMail_WaitsLongerEachTime()
    {
        Mails.Confirmation(account, "t");
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

    private static List<string> Links(string body) => [.. Link().Matches(body).Select(match => match.Value)];

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