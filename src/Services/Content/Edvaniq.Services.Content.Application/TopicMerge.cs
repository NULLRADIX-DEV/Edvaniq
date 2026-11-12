using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The topics of a document asked in parts (TopicBatches) as one list, as if the AI had seen it whole: a topic that comes
// again in a later part, by the same name after TopicKey, takes the sections of it, a subtopic under its topic likewise.
// Everything else follows in the order it came. Each part keeps only its quota of topics, the sections of a topic that
// doesn't fit go to its topic or to the topic before it, so no section is lost. Knowledge merges by the same rule. The key
// terms of a topic (#292) come from all its parts, each term once, TopicAnalysis.MaxTerms at most; a topic that doesn't
// fit takes its terms with it, they were checked against its own sections.
public static class TopicMerge
{
    // sectionOrder holds every section of the document in order, each topic's sections come out in that order.
    public static IReadOnlyList<TopicSuggestion> Merge(
        IReadOnlyList<IReadOnlyList<TopicSuggestion>> batches, IReadOnlyList<int> quotas, IReadOnlyList<Guid> sectionOrder)
    {
        var position = sectionOrder.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
        var topics = new List<Topic>();
        for (var batch = 0; batch < batches.Count; batch++)
        {
            var kept = Trim(batches[batch], quotas[batch]);
            var inBatch = new Topic[kept.Count];
            for (var index = 0; index < kept.Count; index++)
            {
                var (name, parent, sections, terms) = kept[index];
                if (parent is null)
                {
                    var topic = topics.Find(candidate => candidate.Key == TopicKey.Of(name)) ?? Add(topics, name);
                    topic.Sections.AddRange(sections);
                    topic.Terms.AddRange(terms);
                    inBatch[index] = topic;
                }
                else
                {
                    var owner = inBatch[parent.Value];
                    var subtopic = owner.Subtopics.Find(candidate => candidate.Key == TopicKey.Of(name)) ?? Add(owner.Subtopics, name);
                    subtopic.Sections.AddRange(sections);
                    subtopic.Terms.AddRange(terms);
                    inBatch[index] = owner;
                }
            }
        }

        var merged = new List<TopicSuggestion>();
        foreach (var topic in topics)
        {
            var index = merged.Count;
            merged.Add(new TopicSuggestion(topic.Name, null, Ordered(topic.Sections, position), Terms(topic.Terms)));
            merged.AddRange(topic.Subtopics.Select(subtopic =>
                new TopicSuggestion(subtopic.Name, index, Ordered(subtopic.Sections, position), Terms(subtopic.Terms))));
        }

        return merged;
    }

    // The first quota suggestions in the order of the answer (topics, each followed by its subtopics). A dropped
    // subtopic gives its sections to its topic if that stays, any other dropped one to the last topic that stays.
    internal static List<(string Name, int? Parent, List<Guid> Sections, IReadOnlyList<KeyTermSuggestion> Terms)> Trim(
        IReadOnlyList<TopicSuggestion> suggestions, int quota)
    {
        var kept = new List<(string Name, int? Parent, List<Guid> Sections, IReadOnlyList<KeyTermSuggestion> Terms)>();
        var index = new int?[suggestions.Count];
        var lastTopic = -1;
        for (var at = 0; at < suggestions.Count; at++)
        {
            var suggestion = suggestions[at];
            var parent = suggestion.Parent is { } of ? index[of] : null;
            if (kept.Count < quota && (suggestion.Parent is null || parent is not null))
            {
                index[at] = kept.Count;
                kept.Add((suggestion.Name, parent, [.. suggestion.SectionIds], suggestion.Terms ?? []));
                if (suggestion.Parent is null)
                {
                    lastTopic = kept.Count - 1;
                }

                continue;
            }

            kept[parent ?? lastTopic].Sections.AddRange(suggestion.SectionIds);
        }

        return kept;
    }

    private static Topic Add(List<Topic> topics, string name)
    {
        var topic = new Topic(name, TopicKey.Of(name));
        topics.Add(topic);
        return topic;
    }

    private static List<KeyTermSuggestion>? Terms(List<KeyTermSuggestion> terms) =>
        terms.Count == 0
            ? null
            : [.. terms.DistinctBy(term => term.Term, StringComparer.OrdinalIgnoreCase).Take(TopicAnalysis.MaxTerms)];

    private static List<Guid> Ordered(List<Guid> sections, Dictionary<Guid, int> position) =>
        [.. sections.Distinct().OrderBy(section => position.GetValueOrDefault(section, int.MaxValue))];

    private sealed class Topic(string name, string key)
    {
        public string Name { get; } = name;

        public string Key { get; } = key;

        public List<Guid> Sections { get; } = [];

        public List<KeyTermSuggestion> Terms { get; } = [];

        public List<Topic> Subtopics { get; } = [];
    }
}