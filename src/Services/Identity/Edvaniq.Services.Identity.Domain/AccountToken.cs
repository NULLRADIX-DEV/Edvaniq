using System.Security.Cryptography;
using System.Text;

namespace Edvaniq.Services.Identity.Domain;

// The token of a link in a mail (#250, #253). Identity keeps only its hash, the token itself is in the mail and nowhere
// else. It holds for one use, until it runs out or a newer one of the same purpose replaces it.
public sealed class AccountToken
{
    public const int HashLength = 64;

    private AccountToken(Guid accountId, TokenPurpose purpose, string hash, DateTime createdAt)
    {
        AccountId = accountId;
        Purpose = purpose;
        Hash = hash;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + LifetimeOf(purpose);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid AccountId { get; private set; }

    public TokenPurpose Purpose { get; private set; }

    // SHA-256 of the token, hex.
    public string Hash { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    // UTC, when it was used or replaced by a newer one.
    public DateTime? UsedAt { get; private set; }

    public static TimeSpan LifetimeOf(TokenPurpose purpose) => purpose switch
    {
        TokenPurpose.Confirmation => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(1),
    };

    // The new token and the token itself, 256 random bits, URL-safe.
    public static (AccountToken Token, string Value) Create(Account account, TokenPurpose purpose, DateTime createdAt)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new AccountToken(account.Id, purpose, HashOf(value), createdAt), value);
    }

    public static string HashOf(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public bool IsExpired(DateTime now) => now >= ExpiresAt;

    // Used up, or replaced by a newer one.
    public void Use(DateTime now) => UsedAt ??= now;
}

public enum TokenPurpose
{
    Confirmation,
    PasswordReset,
}