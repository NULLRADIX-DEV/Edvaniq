using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// A summary of TopicSummaryGeneration as long as the middle of the frame the prompt asks for: the words of the material
// in their order, round again when there are fewer, and every section as a source. Each length so comes out clearly
// longer than the one before, whatever the material.
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
        var words = sections
            .SelectMany(match => WebUtility.HtmlDecode(match.Groups[2].Value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(word => !word.StartsWith('#'))
            .ToList();
        if (words.Count == 0)
        {
            words = ["Material"];
        }

        var text = string.Join(' ', Enumerable.Range(0, target).Select(index => words[index % words.Count]));
        var used = sections.Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(1).ToList();
        return JsonSerializer.Serialize(new { summary = text, sections = used }, JsonSerializerOptions.Web);
    }

    [GeneratedRegex(@"mit (\d+) bis (\d+) Wörtern")]
    private static partial Regex FramePattern();

    [GeneratedRegex("<section id=\"(\\d+)\">\n(.*?)\n</section>", RegexOptions.Singleline)]
    private static partial Regex SectionPattern();
}