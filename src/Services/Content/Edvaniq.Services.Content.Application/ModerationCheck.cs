using System.Text.RegularExpressions;

namespace Edvaniq.Services.Content.Application;

// What the check of material and AI output decides (#298), without the AI itself. The classifier answers "safe", or
// "unsafe" with the codes of its categories on the next line. Only the categories below stop anything: the others
// (crimes without violence, advice, privacy, elections …) are everyday school material. Violent crimes and weapons
// count as the classifier defines them, as instructions or encouragement, not a history lesson about a war.
public static partial class ModerationCheck
{
    // The first line of what the simulated provider is told, and the purpose in the cost log.
    public const string Task = "edvaniq:moderation";

    public const string PromptVersion = "moderation-1";

    // How much text goes into one check. Sections stay whole, so a longer one makes its own package.
    public const int DefaultPackageCharacters = 20_000;

    // Violent crimes, sex-related crimes, child sexual exploitation, indiscriminate weapons, hate, self-harm, sexual
    // content.
    public static readonly IReadOnlySet<string> Blocked = new HashSet<string>(StringComparer.Ordinal)
    {
        "S1", "S3", "S4", "S9", "S10", "S11", "S12",
    };

    // The code of a hit of the local list, it never comes from the classifier.
    public const string Local = "local";

    // The blocked codes of an answer, empty for "safe" or only categories that don't count here. Null if the answer is
    // neither, so it is asked again and never taken as safe.
    public static IReadOnlyList<string>? Read(string answer)
    {
        var lines = answer.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var verdict = lines[0].ToLowerInvariant();
        if (verdict == "safe")
        {
            return [];
        }

        if (verdict != "unsafe" || lines.Length < 2)
        {
            return null;
        }

        var codes = lines[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(code => code.ToUpperInvariant())
            .ToList();
        return codes.Count == 0 || !codes.All(code => CodePattern().IsMatch(code))
            ? null
            : [.. codes.Where(Blocked.Contains).Distinct()];
    }

    // The texts in packages of at most maxCharacters, in their order. A text longer than that is a package of its own.
    public static IReadOnlyList<string> Packages(IReadOnlyList<string> texts, int maxCharacters)
    {
        var packages = new List<string>();
        var current = new List<string>();
        var length = 0;
        foreach (var text in texts.Where(text => !string.IsNullOrWhiteSpace(text)))
        {
            if (current.Count > 0 && length + text.Length > maxCharacters)
            {
                packages.Add(string.Join("\n\n", current));
                current.Clear();
                length = 0;
            }

            current.Add(text);
            length += text.Length + 2;
        }

        if (current.Count > 0)
        {
            packages.Add(string.Join("\n\n", current));
        }

        return packages;
    }

    // Whether a text holds a word of the local list. Only words that have no place in learning material in any
    // context: insults, never a topic like a war, a crime or sex education, which only the classifier can judge.
    public static bool HitsLocalList(string text) => LocalList().IsMatch(text);

    [GeneratedRegex(@"^S\d{1,2}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"\b(huren(sohn|söhne|soehne|kind)|fotzen?|wichser|missgeburt(en)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalList();
}