using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// Topics from the headings of the numbered sections of TopicAnalysis.User: a line with "# " is a topic, one with "## " a
// subtopic of the topic above, or a topic of its own while no "# " came before. A section belongs to the last heading up to its end, a section before the first heading
// to none. Material without headings gets a topic per section, named after the first three words. Five topics at most.
internal sealed partial class SimulatedTopics : ISimulatedAnswer
{
    private const int MaxTopics = 5;

    public bool Answers(SimulatedPrompt prompt) => ContentAi.IsTask(prompt, ContentAi.TopicsTask);

    public string Answer(SimulatedPrompt prompt)
    {
        var sections = SectionPattern().Matches(prompt.User)
            .Select(match => (Number: int.Parse(match.Groups[1].Value), Lines: match.Groups[2].Value.Split('\n', StringSplitOptions.TrimEntries)))
            .ToList();
        var topics = new List<Topic>();
        List<int>? current = null;
        var topLevel = false;
        foreach (var (number, lines) in sections)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal) && topLevel)
                {
                    var subtopic = new Subtopic(Title(line[3..]), []);
                    topics[^1].Subtopics.Add(subtopic);
                    current = subtopic.Sections;
                }
                else if (line.StartsWith("# ", StringComparison.Ordinal) || line.StartsWith("## ", StringComparison.Ordinal))
                {
                    // Material whose headings all start at "## " has its topics there.
                    topLevel |= line[1] == ' ';
                    var topic = new Topic(Title(line.TrimStart('#')), [], []);
                    topics.Add(topic);
                    current = topic.Sections;
                }
            }

            current?.Add(number);
        }

        if (topics.Count == 0)
        {
            topics.AddRange(sections.Select(section =>
                new Topic(Title(string.Join(' ', section.Lines[0].TrimStart('#').Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3))), [section.Number], [])));
        }

        return JsonSerializer.Serialize(new { topics = topics.Take(MaxTopics) }, JsonSerializerOptions.Web);
    }

    private static string Title(string text) => WebUtility.HtmlDecode(text.Trim());

    [GeneratedRegex("<section id=\"(\\d+)\">\n(.*?)\n</section>", RegexOptions.Singleline)]
    private static partial Regex SectionPattern();

    private sealed record Topic(string Title, List<int> Sections, List<Subtopic> Subtopics);

    private sealed record Subtopic(string Title, List<int> Sections);
}
