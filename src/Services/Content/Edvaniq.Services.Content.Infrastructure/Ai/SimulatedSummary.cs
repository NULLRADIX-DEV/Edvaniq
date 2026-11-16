using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// A summary of TopicSummaryGeneration as long as the middle of the frame the prompt asks for: "Das Material behandelt X."
// for each heading of the material in turn, round again until the words are reached, and every section as a source.
// Each length so comes out clearly longer than the one before, whatever the material. Like the other doubles it never
// quotes the material's sentences, so text slipped into the material can't turn up in a summary as such.
//
// On purpose this double is gullible (SimulatedFence): once anything gets out of the material block, its summary is
// ContentAi.SimulatedHijack.
internal sealed partial class SimulatedSummary : ISimulatedAnswer
{
    public bool Answers(SimulatedPrompt prompt) => ContentAi.IsTask(prompt, ContentAi.SummaryTask);

    public string Answer(SimulatedPrompt prompt)
    {
        var frame = FramePattern().Match(prompt.System);
        var target = frame.Success
            ? (int.Parse(frame.Groups[1].Value, CultureInfo.InvariantCulture) + int.Parse(frame.Groups[2].Value, CultureInfo.InvariantCulture)) / 2
            : 50;
        var sections = SectionPattern().Matches(prompt.User);
        string[] sentences = SimulatedFence.Holds(prompt.User)
            ? [.. Headings(sections).DefaultIfEmpty("das Material").Select(heading => $"Das Material behandelt {heading}.")]
            : [ContentAi.SimulatedHijack];
        var words = sentences.SelectMany(sentence => sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();

        var text = string.Join(' ', Enumerable.Range(0, target).Select(index => words[index % words.Count]));
        var used = sections.Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(1).ToList();
        return JsonSerializer.Serialize(new { summary = text, sections = used }, JsonSerializerOptions.Web);
    }

    // The headings of the material in their order, each once.
    private static IEnumerable<string> Headings(MatchCollection sections) =>
        sections
            .SelectMany(match => match.Groups[2].Value.Split('\n', StringSplitOptions.TrimEntries))
            .Where(line => line.StartsWith('#'))
            .Select(line => WebUtility.HtmlDecode(line.TrimStart('#', ' ')))
            .Where(heading => heading.Length > 0)
            .Distinct(StringComparer.Ordinal);

    [GeneratedRegex(@"mit (\d+) bis (\d+) Wörtern")]
    private static partial Regex FramePattern();

    [GeneratedRegex("<section id=\"(\\d+)\">\n(.*?)\n</section>", RegexOptions.Singleline)]
    private static partial Regex SectionPattern();
}