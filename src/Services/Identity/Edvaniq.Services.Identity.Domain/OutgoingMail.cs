namespace Edvaniq.Services.Identity.Domain;

// A system mail on its way (#248). It is written in the same save as what it tells about, so no mail gets lost when the
// mail service is away, and it goes with its account. It holds a link with a token, so it lives only until it is sent
// or MaxAge has passed, sent or not.
public sealed class OutgoingMail
{
    public const int SubjectMaxLength = 200;

    public const int BodyMaxLength = 4_000;

    // As long as the longest link in a mail is good for (a confirmation, #250).
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    // The waits after a failed attempt, the last one again and again until MaxAge.
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    private OutgoingMail(Guid accountId, MailKind kind, string to, string subject, string body, DateTime createdAt)
    {
        AccountId = accountId;
        Kind = kind;
        To = to;
        Subject = subject;
        Body = body;
        CreatedAt = createdAt;
        DueAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid AccountId { get; private set; }

    public MailKind Kind { get; private set; }

    // The address, normalized (AccountEmail). Never logged.
    public string To { get; private set; }

    public string Subject { get; private set; }

    // Plain text with the link, never logged.
    public string Body { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // UTC, when the next attempt is due.
    public DateTime DueAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTime ExpiresAt => CreatedAt + MaxAge;

    public static OutgoingMail For(Account account, MailKind kind, string subject, string body, DateTime createdAt) =>
        new(account.Id, kind, account.Email ?? throw new InvalidOperationException("A team account gets no mail."), subject, body, createdAt);

    // After a failed attempt: the next one after the next wait.
    public void Postpone(DateTime now)
    {
        DueAt = now + RetryDelays[Math.Min(Attempts, RetryDelays.Count - 1)];
        Attempts++;
    }
}

public enum MailKind
{
    // The link that confirms the address (#250).
    Confirmation,

    // Someone registered with an address that has an account already (#249).
    AlreadyRegistered,

    // The link to a new password (#253).
    PasswordReset,
}