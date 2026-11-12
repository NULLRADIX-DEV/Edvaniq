using System.Security.Cryptography;
using System.Text;

namespace Edvaniq.Services.Identity.Domain;

// An invitation to register (#245). Identity keeps only the hash of the code, the operator sees the code once when it's
// made and after that only its label. Who redeemed it isn't kept, only when.
public sealed class InviteCode
{
    // Crockford's base32: no I, L, O or U, so nothing reads as another letter or as a word.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int CodeLength = 12;

    public const int LabelLength = 6;

    private InviteCode(string label, string codeHash, DateTime createdAt)
    {
        Label = label;
        CodeHash = codeHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    // Names the code in the list and when revoking it, says nothing about the code.
    public string Label { get; private set; }

    // SHA-256 of the normalized code, hex.
    public string CodeHash { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public DateTime? RedeemedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public InviteStatus Status => RevokedAt is not null ? InviteStatus.Revoked
        : RedeemedAt is not null ? InviteStatus.Redeemed
        : InviteStatus.Open;

    // The new code and the code itself, written like K7QX-4M2P-9ZTB. 60 random bits.
    public static (InviteCode Invite, string Code) Create(DateTime createdAt)
    {
        var code = Random(CodeLength);
        return (new InviteCode(Random(LabelLength), HashOf(code), createdAt), $"{code[..4]}-{code[4..8]}-{code[8..]}");
    }

    // What someone types, as stored: case, dashes and spaces don't count, O reads as 0, I and L as 1. Null if it can't
    // be a code.
    public static string? Normalize(string? typed)
    {
        if (typed is null)
        {
            return null;
        }

        var code = new StringBuilder(CodeLength);
        foreach (var c in typed.ToUpperInvariant())
        {
            var mapped = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (mapped is '-' or ' ')
            {
                continue;
            }

            if (!Alphabet.Contains(mapped, StringComparison.Ordinal) || code.Length == CodeLength)
            {
                return null;
            }

            code.Append(mapped);
        }

        return code.Length == CodeLength ? code.ToString() : null;
    }

    public static string HashOf(string normalized) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));

    // False if the code is no longer open.
    public bool Revoke(DateTime revokedAt)
    {
        if (Status != InviteStatus.Open)
        {
            return false;
        }

        RevokedAt = revokedAt;
        return true;
    }

    private static string Random(int length) =>
        new([.. Enumerable.Range(0, length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])]);
}

public enum InviteStatus
{
    Open,
    Redeemed,
    Revoked,
}