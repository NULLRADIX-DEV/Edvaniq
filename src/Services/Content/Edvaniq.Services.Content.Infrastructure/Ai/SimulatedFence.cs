using System.Text.RegularExpressions;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// Whether a user message of the simulated provider is the material block of PromptMaterial and nothing else, or that
// block followed by the known questions of QuestionGeneration.User (#1106). The doubles for topics and questions are
// gullible on purpose: once anything gets out of the blocks, they do what it says instead (ContentAi.SimulatedHijack),
// like a model that falls for injection. Only the escaping keeps them from that, so a broken fence makes the injection
// tests red. Don't "fix" it.
internal static partial class SimulatedFence
{
    private const string Open = "<material>\n";

    private const string Close = "</material>";

    private const string KnownOpen = "\n<known>\n";

    private const string KnownClose = "\n</known>";

    // The message is the one block, maybe with the known block after it, and inside them every '<' belongs to a section
    // tag of the material.
    public static bool Holds(string user)
    {
        if (user.EndsWith(KnownClose, StringComparison.Ordinal))
        {
            var at = user.LastIndexOf(KnownOpen, StringComparison.Ordinal);
            if (at < 0 || user[(at + KnownOpen.Length)..^KnownClose.Length].Contains('<', StringComparison.Ordinal))
            {
                return false;
            }

            user = user[..at];
        }

        return user.StartsWith(Open, StringComparison.Ordinal)
            && user.EndsWith(Close, StringComparison.Ordinal)
            && !SectionTagPattern().Replace(user[Open.Length..^Close.Length], "").Contains('<', StringComparison.Ordinal);
    }

    [GeneratedRegex("<section id=\"\\d+\">|</section>")]
    private static partial Regex SectionTagPattern();
}