using System.Security.Cryptography;
using System.Text;

using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.UnitTests;

// The SHA-256 over every variant of a system prompt, one per profile in a fixed order. A changed sentence in any variant
// changes it, so it needs a new prompt version.
internal static class PromptHash
{
    public static string Of(Func<LearnerProfile, string> system) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n\0\n", LearnerProfile.All.Select(profile => $"{profile}\n{system(profile).ReplaceLineEndings("\n")}")))));
}