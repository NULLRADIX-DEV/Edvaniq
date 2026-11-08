using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Edvaniq.Services.Identity.Application;

// PBKDF2 with HMAC-SHA256 (docs/tokens.md#passwörter). The hash names its algorithm and rounds, so a later version can
// raise them or switch the algorithm and still verify the hashes it finds.
public static class PasswordHasher
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 256;

    // OWASP Password Storage Cheat Sheet for PBKDF2-HMAC-SHA256.
    public const int Rounds = 600_000;

    private const string Algorithm = "pbkdf2-sha256";
    private const int SaltLength = 16;
    private const int HashLength = 32;

    // Verified when there is no account, so a wrong name takes as long as a wrong password.
    private static readonly string Dummy = Hash("no account has this password");

    // Why the password can not be used, or null if it can.
    public static string? Check(string password) =>
        password.Length < MinimumLength ? $"The password needs at least {MinimumLength} characters."
        : password.Length > MaximumLength ? $"The password may have at most {MaximumLength} characters."
        : null;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Derive(password, salt, Rounds);
        return string.Join("$", Algorithm, Rounds.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string? stored)
    {
        var parts = (stored ?? Dummy).Split("$");
        if (parts is not [Algorithm, var roundsText, var saltText, var hashText]
            || !int.TryParse(roundsText, NumberStyles.None, CultureInfo.InvariantCulture, out var rounds)
            || rounds <= 0)
        {
            return false;
        }

        var expected = Convert.FromBase64String(hashText);
        var actual = Derive(password, Convert.FromBase64String(saltText), rounds);
        return CryptographicOperations.FixedTimeEquals(actual, expected) && stored is not null;
    }

    private static byte[] Derive(string password, byte[] salt, int rounds) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, rounds, HashAlgorithmName.SHA256, HashLength);
}