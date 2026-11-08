using System.Text.RegularExpressions;

namespace Edvaniq.Services.Identity.Domain;

// Creating an account and logging in both go through Normalize, so "Ben" and " ben " are the same account and the
// login throttle counts them together.
public static partial class AccountName
{
    // The normalized name, or null if it is not a valid one.
    public static string? Normalize(string? name)
    {
        var normalized = name?.Trim().ToLowerInvariant();
        return normalized is not null && Valid().IsMatch(normalized) ? normalized : null;
    }

    [GeneratedRegex(@"^[a-z0-9._-]{1,64}\z")]
    private static partial Regex Valid();
}