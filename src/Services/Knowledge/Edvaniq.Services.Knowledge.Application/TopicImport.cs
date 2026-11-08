using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// The topics and section assignments that DocumentAnalyzed brings. Content already sends two levels and valid names,
// but a handler that throws gets the message again and again, so whatever doesn't fit gets fixed up instead.
public sealed record TopicImport(IReadOnlyList<Topic> Topics, IReadOnlyList<TopicSection> Sections)
{
    public const string UnnamedTopic = "Thema ohne Namen";

    public static TopicImport From(DocumentAnalyzed analysis, Guid learningGoalId, DateTime createdAt)
    {
        var topics = new List<Topic>();
        var sections = new List<TopicSection>();
        foreach (var (suggestion, position) in analysis.Topics.Select((suggestion, position) => (suggestion, position)))
        {
            var name = NameOf(suggestion.Name);

            // A parent that isn't an earlier suggestion makes a topic of the upper level, a subtopic hands over its own.
            var parent = suggestion.Parent is { } index && index >= 0 && index < position ? topics[index] : null;
            if (parent is { IsSubtopic: true })
            {
                parent = topics.First(topic => topic.Id == parent.ParentId);
            }

            var topic = parent is null
                ? Topic.Create(analysis.UserId, learningGoalId, name, createdAt, analysis.DocumentId, position)
                : parent.AddSubtopic(name, createdAt, analysis.DocumentId, position);
            topics.Add(topic);
            sections.AddRange(suggestion.SectionIds.Distinct().Select(topic.AssignSection));
        }

        return new(topics, sections);
    }

    private static string NameOf(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length == 0 ? UnnamedTopic
            : trimmed.Length > Topic.NameMaxLength ? trimmed[..Topic.NameMaxLength].TrimEnd()
            : trimmed;
    }
}