using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

public interface IMailOutbox
{
    void Add(OutgoingMail mail);

    // Whether a mail waits since before this time, a sign that the mail service is away.
    Task<bool> AnyWaitingSinceAsync(DateTime before, CancellationToken cancellationToken);
}

// The pages of the web app a mail links to, under its public HTTPS address (Mail:PublicBaseUrl). A token goes into the
// fragment: the browser never sends it to a server, so it ends up in no access log and no Referer.
public sealed class MailLinks(Uri publicBase)
{
    public string Login => new Uri(publicBase, "login").ToString();

    public string Confirmation(string token) => new Uri(publicBase, "konto/bestaetigen").ToString() + "#t=" + Uri.EscapeDataString(token);

    public string PasswordReset(string token) => new Uri(publicBase, "konto/passwort-neu").ToString() + "#t=" + Uri.EscapeDataString(token);
}

// The system mails of Identity (#248): short German plain text a young person understands, no HTML, no images and no
// link but the ones to the app, so nothing tracks who opens them. Each goes into the outbox in the save of the caller.
public sealed class SystemMails(IMailOutbox outbox, MailLinks links, TimeProvider time)
{
    // A mail that waits longer than this tells the user it may come late.
    public static readonly TimeSpan DelayedAfter = TimeSpan.FromMinutes(5);

    private const string Signature = "Dein Edvaniq-Team";

    public void Confirmation(Account account, string token) => Add(account, MailKind.Confirmation, "Bestätige deine E-Mail-Adresse", $"""
        Hallo {account.Pseudonym},

        du hast dich bei Edvaniq registriert. Bestätige deine E-Mail-Adresse mit diesem Link:

        {links.Confirmation(token)}

        Der Link gilt 24 Stunden. Danach kannst du in der App einen neuen anfordern.

        Du hast dich nicht registriert? Dann musst du nichts tun. Ohne Bestätigung wird das Konto wieder gelöscht.

        {Signature}
        """);

    public void AlreadyRegistered(Account account) => Add(account, MailKind.AlreadyRegistered, "Du hast schon ein Konto", $"""
        Hallo,

        jemand wollte mit dieser E-Mail-Adresse ein neues Konto bei Edvaniq anlegen. Du hast aber schon eins. Hier meldest du dich an:

        {links.Login}

        Das warst nicht du? Dann musst du nichts tun, an deinem Konto hat sich nichts geändert.

        {Signature}
        """);

    public void PasswordReset(Account account, string token) => Add(account, MailKind.PasswordReset, "Neues Passwort für Edvaniq", $"""
        Hallo {account.Pseudonym},

        mit diesem Link legst du ein neues Passwort fest:

        {links.PasswordReset(token)}

        Der Link gilt eine Stunde.

        Du wolltest kein neues Passwort? Dann musst du nichts tun, dein altes gilt weiter.

        {Signature}
        """);

    // True if mails wait longer than they should: the user hears that theirs may come late.
    public Task<bool> DelayedAsync(CancellationToken cancellationToken) =>
        outbox.AnyWaitingSinceAsync(time.GetUtcNow().UtcDateTime - DelayedAfter, cancellationToken);

    private void Add(Account account, MailKind kind, string subject, string body) =>
        outbox.Add(OutgoingMail.For(account, kind, subject, body.ReplaceLineEndings("\n"), time.GetUtcNow().UtcDateTime));
}