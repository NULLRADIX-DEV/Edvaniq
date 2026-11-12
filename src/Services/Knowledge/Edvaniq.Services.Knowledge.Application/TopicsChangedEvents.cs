using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Knowledge.Application;

// Every save that changes the topics of a learning goal calls this once, before it saves: TopicsChanged then goes out
// with the change or not at all. Every subtopic comes with its parent (#909).
public sealed class TopicsChangedEvents(ITopicState state, IEventOutbox outbox, TimeProvider time)
{
    public async Task AnnounceAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken)
    {
        await state.TouchAsync(ownerId, learningGoalId, cancellationToken);
        var topics = await state.TopicsAsync(ownerId, learningGoalId, cancellationToken);
        var parents = topics
            .Where(topic => topic.ParentId is not null)
            .Select(topic => new TopicParent(topic.Id, topic.ParentId!.Value))
            .ToList();

        // A subtopic of a hidden topic goes with it.
        var hiddenOnes = topics.Where(topic => topic.Hidden).Select(topic => topic.Id).ToHashSet();
        var hidden = topics.ToLookup(topic => hiddenOnes.Contains(topic.Id) || (topic.ParentId is { } parent && hiddenOnes.Contains(parent)));
        outbox.Add(new TopicsChanged(
            ownerId, learningGoalId, Now(), [.. hidden[false].Select(topic => topic.Id)], [.. hidden[true].Select(topic => topic.Id)], parents, Facts(topics)));
    }

    // Every topic, hidden or not, with its place in the material and its sections (#380). The order of the topic list:
    // the topics of the upper level by when they came and their place in the analysis, each followed by its subtopics
    // in the same order. Ties go by name and then id, so the same state always gives the same ranks.
    public static List<TopicFacts> Facts(IReadOnlyList<TopicInState> topics)
    {
        var ids = topics.Select(topic => topic.Id).ToHashSet();
        var ordered = topics
            .OrderBy(topic => topic.CreatedAt)
            .ThenBy(topic => topic.Position)
            .ThenBy(topic => topic.Name, StringComparer.Ordinal)
            .ThenBy(topic => topic.Id)
            .ToList();
        var children = ordered.Where(topic => topic.ParentId is { } parent && ids.Contains(parent)).ToLookup(topic => topic.ParentId!.Value);
        var inMaterialOrder = ordered
            .Where(topic => topic.ParentId is not { } parent || !ids.Contains(parent))
            .SelectMany(topic => children[topic.Id].Prepend(topic));
        return [.. inMaterialOrder.Select((topic, rank) => new TopicFacts(topic.Id, rank, Math.Max(topic.Sections, 0)))];
    }

    // Whole microseconds, what a datetime(6) column keeps. A receiver compares the value it stored with the next one.
    private DateTime Now()
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - (now.Ticks % 10), DateTimeKind.Utc);
    }
}