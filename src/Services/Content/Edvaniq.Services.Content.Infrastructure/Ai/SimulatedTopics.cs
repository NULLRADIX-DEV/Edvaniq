using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// Topics from the headings of the material: a line with "# " is a topic, one with "## " a subtopic of the topic above.
// Material without headings gets a topic per line, named after its first three words. Five topics at most.
internal sealed class SimulatedTopics : ISimulatedAnswer
{
    private const int MaxTopics = 5;

    public bool Answers(SimulatedPrompt prompt) => ContentAi.IsTask(prompt, ContentAi.TopicsTask);

    public string Answer(SimulatedPrompt prompt)
    {
        var lines = prompt.User.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var topics = new List<Topic>();
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal) && topics.Count > 0)
            {
                topics[^1].Subtopics.Add(line[3..].Trim());
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                topics.Add(new Topic(line[2..].Trim(), []));
            }
        }

        if (topics.Count == 0)
        {
            topics.AddRange(lines.Select(line => new Topic(string.Join(' ', line.Split(' ').Take(3)), [])));
        }

        return JsonSerializer.Serialize(new { topics = topics.Take(MaxTopics) }, JsonSerializerOptions.Web);
    }

    private sealed record Topic(string Title, List<string> Subtopics);
}
