using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// 15 to 20 true/false questions on what the numbered sections of QuestionGeneration.User are about, by their headings:
// a section without one takes the heading above it, material without any its first three words. One section after the
// other and round again when there are fewer. How many and how hard comes from the prompt's randomness. The questions
// never quote the material's sentences, so text slipped into the material can't turn up in them as such.
//
// On purpose this double is gullible, like a model that falls for injection: as soon as the user message holds anything
// outside the one <material> block, or a tag inside it other than the sections, it does what the material says instead
// and answers with Hijacked. Only the escaping of PromptMaterial keeps it from that, so a broken fence makes the
// injection tests red. Don't "fix" it.
internal sealed partial class SimulatedQuestions : ISimulatedAnswer
{
    // What a hijacked model writes, taken from the injection of the reference set (tests/ReferenceSet).
    private const string Hijacked = ContentAi.SimulatedHijack;

    private const string Open = "<material>\n";

    private const string Close = "</material>";

    private const int LabelWords = 3;

    public bool Answers(SimulatedPrompt prompt) => ContentAi.IsTask(prompt, ContentAi.QuestionsTask);

    public string Answer(SimulatedPrompt prompt)
    {
        if (!Fenced(prompt.User))
        {
            var hijacked = new { Text = Hijacked, Options = new[] { Hijacked, "Alles erledigt" }, Answer = 0, Explanation = Hijacked, Difficulty = 1, Section = 1 };
            return JsonSerializer.Serialize(new { questions = new[] { hijacked } }, JsonSerializerOptions.Web);
        }

        var labels = Labels(SectionPattern().Matches(prompt.User)
            .Select(match => (Number: int.Parse(match.Groups[1].Value), Lines: match.Groups[2].Value.Split('\n', StringSplitOptions.TrimEntries)))
            .ToList());
        if (labels.Count == 0)
        {
            labels = [(1, "Das Material")];
        }

        var random = prompt.Random();
        var questions = Enumerable.Range(0, random.Next(15, 21))
            .Select(index => labels[index % labels.Count])
            .Select(label => new
            {
                Text = $"Stimmt das: Das Material behandelt {label.Text}?",
                Options = new[] { "Richtig", "Falsch" },
                Answer = 0,
                Explanation = $"Ein Abschnitt des Materials gehört zu {label.Text}.",
                Difficulty = random.Next(1, 4),
                Section = label.Section,
            })
            .ToList();

        return JsonSerializer.Serialize(new { questions }, JsonSerializerOptions.Web);
    }

    // The message is the one block and nothing else, and inside it every '<' belongs to a section tag.
    private static bool Fenced(string user) =>
        user.StartsWith(Open, StringComparison.Ordinal)
        && user.EndsWith(Close, StringComparison.Ordinal)
        && !SectionTagPattern().Replace(user[Open.Length..^Close.Length], "").Contains('<', StringComparison.Ordinal);

    // Per section the last heading up to its end, or its own first words while there was none yet.
    private static List<(int Section, string Text)> Labels(List<(int Number, string[] Lines)> sections)
    {
        var labels = new List<(int Section, string Text)>();
        string? heading = null;
        foreach (var (number, lines) in sections)
        {
            heading = lines.LastOrDefault(line => line.StartsWith('#')) is { } own ? Clean(own.TrimStart('#', ' ')) : heading;
            var first = lines.FirstOrDefault(line => line.Length > 0) ?? "";
            var text = heading ?? Clean(string.Join(' ', first.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(LabelWords))).TrimEnd('.', ',', ':', ';', '!', '?');
            if (text.Length > 0)
            {
                labels.Add((number, text[..Math.Min(text.Length, MaxLabel)]));
            }
        }

        return labels;
    }

    private static string Clean(string text) => WebUtility.HtmlDecode(text).Trim();

    // Leaves room in a question's text for the words around the label.
    private const int MaxLabel = 400;

    [GeneratedRegex("<section id=\"(\\d+)\">\n(.*?)\n</section>", RegexOptions.Singleline)]
    private static partial Regex SectionPattern();

    [GeneratedRegex("<section id=\"\\d+\">|</section>")]
    private static partial Regex SectionTagPattern();
}