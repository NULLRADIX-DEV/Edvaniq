namespace Edvaniq.Services.Identity.Domain;

// A team account (docs/tokens.md). Its id is the sub of every token issued for it, so it never changes. The name is
// only for the login and never leaves Identity.
public sealed class Account
{
    public const int NameMaxLength = 64;

    // Long enough for any hash format the PasswordHasher may write later.
    public const int PasswordHashMaxLength = 256;

    private Account(string name, string passwordHash, DateTime createdAt)
    {
        Name = name;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    // Normalized, see AccountName.
    public string Name { get; private set; }

    public string PasswordHash { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // An account for automated tests: its tokens carry test_double, and Content analyses its material with the
    // simulated AI. Only the operator's account command sets it.
    public bool UsesTestDouble { get; private set; }

    // Call it only with a name from AccountName.Normalize.
    public static Account Create(string name, string passwordHash, DateTime createdAt) => new(name, passwordHash, createdAt);

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void SetTestDouble(bool usesTestDouble) => UsesTestDouble = usesTestDouble;
}