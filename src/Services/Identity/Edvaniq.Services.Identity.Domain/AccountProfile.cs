using System.Text.RegularExpressions;

namespace Edvaniq.Services.Identity.Domain;

// What a user tells about themself at registration (#255): a pseudonym the app calls them by and the year of birth for
// the age rule. Nothing else about a person is asked.
public static partial class AccountProfile
{
    public const int PseudonymMinLength = 3;

    public const int PseudonymMaxLength = 24;

    // Plausible ages in years, by the year alone.
    public const int MinAge = 8;

    public const int MaxAge = 100;

    // A minor until the year of birth lies this many years back (#256): someone born in 2008 counts as of age only in
    // 2027, so whoever might still be 17 counts as a minor.
    public const int AgeOfMajority = 19;

    // The pseudonym trimmed, with single spaces, or null if it breaks the rules: 3 to 24 letters, digits, spaces, dot,
    // dash or underscore, at least one letter. No @, so no address ends up as a name. Not unique.
    public static string? NormalizePseudonym(string? pseudonym)
    {
        var normalized = pseudonym is null ? null : Spaces().Replace(pseudonym.Trim(), " ");
        return normalized is { Length: >= PseudonymMinLength and <= PseudonymMaxLength } && Pseudonym().IsMatch(normalized)
            ? normalized
            : null;
    }

    public static bool IsPlausibleBirthYear(int birthYear, int currentYear) =>
        birthYear >= currentYear - MaxAge && birthYear <= currentYear - MinAge;

    public static bool IsMinor(int birthYear, int currentYear) => currentYear - birthYear < AgeOfMajority;

    [GeneratedRegex(@"^(?=.*\p{L})[\p{L}\p{N} ._-]+\z")]
    private static partial Regex Pseudonym();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}