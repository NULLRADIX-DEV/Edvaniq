using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Edvaniq.Services.Identity.Domain;

// The login of a user account (#244). Stored and compared trimmed and in lower case, nothing else is changed, so
// "a+b@x.de" stays its own address. Never logged: where an address must be told apart, Fingerprint stands for it.
public static partial class AccountEmail
{
    // RFC 5321 allows no longer path.
    public const int MaxLength = 254;

    // The normalized address, or null if it can't be one.
    public static string? Normalize(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        return normalized is { Length: <= MaxLength } && Valid().IsMatch(normalized) ? normalized : null;
    }

    // A login name with an @ is an address, a team account's name never has one.
    public static bool IsEmail(string? login) => login?.Contains('@', StringComparison.Ordinal) == true;

    // SHA-256 of the normalized address, for keys that must not hold the address itself.
    public static string Fingerprint(string normalized) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+\z")]
    private static partial Regex Valid();
}