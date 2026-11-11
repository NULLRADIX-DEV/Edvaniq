using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// Rules 1 to 4 of tests/ReferenceSet/README.md for one analysis of a reference document: which expected topics the
// suggestions hit, on which level, whether the anchors sit with their topic, and what is invented. The sections are
// those of the analysis, the suggestions name them by the ids given in the same order.
internal sealed record ReferenceScore(int Hits, int Expected, int Invented, int LevelErrors, int AnchorsMet, int Anchors)
{
    public override string ToString() =>
        $"hits {Hits}/{Expected}, invented {Invented}, level errors {LevelErrors}, anchors {AnchorsMet}/{Anchors}";

    public static ReferenceScore Of(
        ReferenceDocument document,
        string text,
        IReadOnlyList<TextSpan> sections,
        IReadOnlyList<Guid> sectionIds,
        IReadOnlyList<TopicSuggestion> suggestions)
    {
        var expected = document.AllTopics().ToList();

        // Rule 1 and 4: in the order of the analysis, each expected topic once, anything else is invented.
        var hitBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var invented = 0;
        var levelErrors = 0;
        for (var index = 0; index < suggestions.Count; index++)
        {
            var name = ReferenceSet.Normalize(suggestions[index].Name);
            var hit = expected.FirstOrDefault(entry =>
                !hitBy.ContainsKey(entry.Path) && entry.Topic.Names.Any(expectedName => ReferenceSet.Normalize(expectedName) == name));
            if (hit.Path is null)
            {
                invented++;
                continue;
            }

            hitBy[hit.Path] = index;

            // Rule 2: a hit on the other level counts, but as a level error.
            if (IsSubtopic(hit.Path) != suggestions[index].Parent is not null)
            {
                levelErrors++;
            }
        }

        // Rule 3: the section of an anchor belongs to the suggestion that hit its topic, or to a subtopic of it.
        var owner = new int?[sections.Count];
        for (var index = 0; index < suggestions.Count; index++)
        {
            foreach (var id in suggestions[index].SectionIds)
            {
                var section = Index(sectionIds, id);
                if (section >= 0)
                {
                    owner[section] = index;
                }
            }
        }

        var anchors = 0;
        var met = 0;
        foreach (var (topic, path) in expected)
        {
            foreach (var anchor in topic.Anchors ?? [])
            {
                anchors++;
                var at = text.IndexOf(anchor, StringComparison.Ordinal);
                var section = Index(sections, at);
                if (section >= 0 && hitBy.TryGetValue(path, out var hit) && owner[section] is { } holder
                    && (holder == hit || suggestions[holder].Parent == hit))
                {
                    met++;
                }
            }
        }

        return new ReferenceScore(hitBy.Count, expected.Count, invented, levelErrors, met, anchors);
    }

    private static bool IsSubtopic(string path) => path.Contains(" / ", StringComparison.Ordinal);

    private static int Index(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            if (ids[index] == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static int Index(IReadOnlyList<TextSpan> sections, int at)
    {
        for (var index = 0; index < sections.Count; index++)
        {
            if (at >= sections[index].Start && at < sections[index].End)
            {
                return index;
            }
        }

        return -1;
    }
}