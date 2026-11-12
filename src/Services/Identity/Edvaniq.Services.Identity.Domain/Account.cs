namespace Edvaniq.Services.Identity.Domain;

// An account (docs/tokens.md). Its id is the sub of every token issued for it, so it never changes. A team account logs
// in with its name, a user account with its email address. Name, address, pseudonym and birth year never leave Identity
// in a token.
public sealed class Account
{
    public const int NameMaxLength = 64;

    public const int PseudonymMaxLength = 32;

    // Long enough for any hash format the PasswordHasher may write later.
    public const int PasswordHashMaxLength = 256;

    private Account(AccountKind kind, string? name, string? email, string passwordHash, DateTime createdAt)
    {
        Kind = kind;
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public AccountKind Kind { get; private set; }

    // A team account's login, normalized, see AccountName. Null for a user account.
    public string? Name { get; private set; }

    // A user account's login, normalized, see AccountEmail. Null for a team account.
    public string? Email { get; private set; }

    // What the app calls a user (#255). Null for a team account.
    public string? Pseudonym { get; private set; }

    // Only the year, for the age rule (#256). Null for a team account.
    public int? BirthYear { get; private set; }

    // A team account is always active.
    public AccountStatus Status { get; private set; } = AccountStatus.Active;

    public string PasswordHash { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // UTC, when the address was confirmed (#250).
    public DateTime? ConfirmedAt { get; private set; }

    // UTC, when the operator let a minor in (#256).
    public DateTime? ApprovedAt { get; private set; }

    // An account for automated tests: its tokens carry test_double, and Content analyses its material with the
    // simulated AI. Only the operator's account command sets it.
    public bool UsesTestDouble { get; private set; }

    // Call it only with a name from AccountName.Normalize.
    public static Account Create(string name, string passwordHash, DateTime createdAt) =>
        new(AccountKind.Team, name, null, passwordHash, createdAt);

    // A new user account waits for its address to be confirmed. Call it only with an address from AccountEmail.Normalize.
    public static Account CreateUser(string email, string pseudonym, int birthYear, string passwordHash, DateTime createdAt) =>
        new(AccountKind.User, null, email, passwordHash, createdAt)
        {
            Pseudonym = pseudonym,
            BirthYear = birthYear,
            Status = AccountStatus.Unconfirmed,
        };

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void SetTestDouble(bool usesTestDouble) => UsesTestDouble = usesTestDouble;
}

public enum AccountKind
{
    Team,
    User,
}

public enum AccountStatus
{
    Active,

    // The address isn't confirmed yet (#250).
    Unconfirmed,

    // A minor waits for the operator's approval (#256).
    AwaitingApproval,
}