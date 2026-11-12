using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// What DocumentAnalyzed brings into a learning goal: the topics that are new there and the sections of the document,
// each with the topic it belongs to. A suggestion is the same topic as one the goal has when it sits on the same level,
// under the same topic, and its name matches after TopicName.Normalize, the current name or the one the analysis gave
// first. Then it takes that topic, id and name, and only hangs its sections there. Two equal suggestions in one analysis
// are one topic too. Content already sends two levels and valid names, but a handler that throws gets the message again
// and again, so whatever doesn't fit gets fixed up instead. The key terms of the analysis (#292) go to the topic each
// suggestion resolved to, a term once per topic and document, whatever case it is written in.
public sealed record TopicImport(
    string OwnerId, Guid DocumentId, IReadOnlyList<Topic> Topics, IReadOnlyList<TopicSection> Sections, IReadOnlyList<KeyTerm>? Terms = null)
{
    public const string UnnamedTopic = "Thema ohne Namen";

    // Known are the user's topics of the learning goal, those of other users and goals never match.
    public static TopicImport From(DocumentAnalyzed analysis, Guid learningGoalId, DateTime createdAt, IReadOnlyList<Topic> known)
    {
        // The oldest topic wins if there are several of one name, and a new one comes after all known ones.
        var candidates = known
            .Where(topic => topic.OwnerId == analysis.UserId && topic.LearningGoalId == learningGoalId)
            .OrderBy(topic => topic.CreatedAt)
            .ThenBy(topic => topic.Position)
            .ToList();
        var created = new List<Topic>();
        var resolved = new List<Topic>();
        var sections = new List<TopicSection>();
        var terms = new List<KeyTerm>();
        foreach (var (suggestion, position) in analysis.Topics.Select((suggestion, position) => (suggestion, position)))
        {
            var name = NameOf(suggestion.Name);

            // A parent that isn't an earlier suggestion makes a topic of the upper level, a subtopic hands over its own.
            var parent = suggestion.Parent is { } index && index >= 0 && index < position ? resolved[index] : null;
            if (parent is { IsSubtopic: true })
            {
                parent = candidates.First(topic => topic.Id == parent.ParentId);
            }

            var topic = candidates.FirstOrDefault(topic => topic.ParentId == parent?.Id && topic.IsCalled(name));
            if (topic is null)
            {
                topic = parent is null
                    ? Topic.Create(analysis.UserId, learningGoalId, name, createdAt, analysis.DocumentId, position)
                    : parent.AddSubtopic(name, createdAt, analysis.DocumentId, position);
                created.Add(topic);
                candidates.Add(topic);
            }

            resolved.Add(topic);
            sections.AddRange(suggestion.SectionIds
                .Where(section => !sections.Any(assigned => assigned.TopicId == topic.Id && assigned.SectionId == section))
                .Distinct()
                .Select(section => topic.AssignSection(analysis.DocumentId, section)));
            foreach (var term in suggestion.Terms ?? [])
            {
                if (!string.IsNullOrWhiteSpace(term.Term) && !string.IsNullOrWhiteSpace(term.Definition)
                    && !terms.Any(other => other.TopicId == topic.Id
                        && string.Equals(other.Term, term.Term.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    terms.Add(topic.KeyTerm(analysis.DocumentId, term.SectionId, term.Term, term.Definition, createdAt, terms.Count));
                }
            }
        }

        return new(analysis.UserId, analysis.DocumentId, created, sections, terms);
    }

    private static string NameOf(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length == 0 ? UnnamedTopic
            : trimmed.Length > Topic.NameMaxLength ? trimmed[..Topic.NameMaxLength].TrimEnd()
            : trimmed;
    }
}