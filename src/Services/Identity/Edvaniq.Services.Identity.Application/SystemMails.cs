using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

public interface IMailOutbox
{
    void Add(OutgoingMail mail);

    // Whether a mail waits since before this time, a sign that the mail service is away.
    Task<bool> AnyWaitingSinceAsync(DateTime before, CancellationToken cancellationToken);
}

public interface IAccountTokens
{
    void Add(AccountToken token);

    Task<AccountToken?> FindByHashAsync(string hash, CancellationToken cancellationToken);

    // Of the account and purpose, made since then, newest first.
    Task<IReadOnlyList<AccountToken>> SinceAsync(Guid accountId, TokenPurpose purpose, DateTime since, CancellationToken cancellationToken);
}

// The pages of the web app a mail links to, under its public HTTPS address (Mail:PublicBaseUrl). A token goes into the
// fragment: the browser never sends it to a server, so it ends up in no access log and no Referer.
public sealed class MailLinks(Uri publicBase)
{
    public string Login => new Uri(publicBase, "login").ToString();

    public string Confirmation(string token) => new Uri(publicBase, "konto/bestaetigen").ToString() + "#t=" + Uri.EscapeDataString(token);

    public string PasswordReset(string token) => new Uri(publicBase, "konto/passwort-neu").ToString() + "#t=" + Uri.EscapeDataString(token);
}

// The text of a mail, written when it is sent.
public sealed record MailContent(string Subject, string Body);

// The system mails of Identity (#248): short German plain text a young person understands, no HTML, no images and no
// link but the ones to the app, so nothing tracks who opens them. No pseudonym either: it is the user's own text and
// would let anyone put a line of their choice into a mail from Edvaniq. Asking for a mail puts it into the outbox in the
// save of the caller, the text and the token of its link come only when it is sent (MailDispatcher).
public sealed class SystemMails(IMailOutbox outbox, TimeProvider time)
{
    // A mail that waits longer than this tells the user it may come late.
    public static readonly TimeSpan DelayedAfter = TimeSpan.FromMinutes(5);

    private const string Signature = "Dein Edvaniq-Team";

    public void Confirmation(Account account) => Add(account, MailKind.Confirmation);

    public void AlreadyRegistered(Account account) => Add(account, MailKind.AlreadyRegistered);

    public void PasswordReset(Account account) => Add(account, MailKind.PasswordReset);

    // True if mails wait longer than they should: the user hears that theirs may come late.
    public Task<bool> DelayedAsync(CancellationToken cancellationToken) =>
        outbox.AnyWaitingSinceAsync(time.GetUtcNow().UtcDateTime - DelayedAfter, cancellationToken);

    // The purpose of the token a mail of this kind carries, null for one without a link to a token.
    public static TokenPurpose? TokenOf(MailKind kind) => kind switch
    {
        MailKind.Confirmation => TokenPurpose.Confirmation,
        MailKind.PasswordReset => TokenPurpose.PasswordReset,
        _ => null,
    };

    // The text of a mail of this kind, with the token of its link if it has one.
    public static MailContent Write(MailKind kind, MailLinks links, string? token) => kind switch
    {
        MailKind.Confirmation => new("Bestätige deine E-Mail-Adresse", Text($"""
            Hallo,

            du hast dich bei Edvaniq registriert. Bestätige deine E-Mail-Adresse mit diesem Link:

            {links.Confirmation(token!)}

            Der Link gilt 24 Stunden. Danach kannst du in der App einen neuen anfordern.

            Du hast dich nicht registriert? Dann musst du nichts tun. Ohne Bestätigung wird das Konto nach 7 Tagen gelöscht.
            """)),
        MailKind.AlreadyRegistered => new("Du hast schon ein Konto", Text($"""
            Hallo,

            jemand wollte mit dieser E-Mail-Adresse ein neues Konto bei Edvaniq anlegen. Du hast aber schon eins. Hier meldest du dich an:

            {links.Login}

            Das warst nicht du? Dann musst du nichts tun, an deinem Konto hat sich nichts geändert.
            """)),
        _ => new("Neues Passwort für Edvaniq", Text($"""
            Hallo,

            mit diesem Link legst du ein neues Passwort fest:

            {links.PasswordReset(token!)}

            Der Link gilt eine Stunde.

            Du wolltest kein neues Passwort? Dann musst du nichts tun, dein altes gilt weiter.
            """)),
    };

    private static string Text(string body) => (body + "\n\n" + Signature).ReplaceLineEndings("\n");

    private void Add(Account account, MailKind kind) => outbox.Add(OutgoingMail.For(account, kind, time.GetUtcNow().UtcDateTime));
}