using System.Text.RegularExpressions;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// Whether a user message of the simulated provider is the material block of PromptMaterial and nothing else. The doubles
// for topics and questions are gullible on purpose: once anything gets out of the block, they do what it says instead
// (ContentAi.SimulatedHijack), like a model that falls for injection. Only the escaping keeps them from that, so a broken
// fence makes the injection tests red. Don't "fix" it.
internal static partial class SimulatedFence
{
    private const string Open = "<material>\n";

    private const string Close = "</material>";

    // The message is the one block, and inside it every '<' belongs to a section tag.
    public static bool Holds(string user) =>
        user.StartsWith(Open, StringComparison.Ordinal)
        && user.EndsWith(Close, StringComparison.Ordinal)
        && !SectionTagPattern().Replace(user[Open.Length..^Close.Length], "").Contains('<', StringComparison.Ordinal);

    [GeneratedRegex("<section id=\"\\d+\">|</section>")]
    private static partial Regex SectionTagPattern();
}