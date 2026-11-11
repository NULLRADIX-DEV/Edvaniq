using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Takes over the state of a learning goal's topics (docs/spec/events.md): adds the new ones, drops those in neither
// list. The inbox drops a redelivered message, the snapshot the same state under a new message id and an older state
// that arrives after a newer one. If two arrive at once, the key or the snapshot's token makes the later save fail and
// the broker delivers it again. Of the parents it keeps only which active topic has an active subtopic (#909, rule 5).
public sealed class TopicsChangedHandler(IKnownTopics topics) : IIntegrationEventHandler<TopicsChanged>
{
    public async Task HandleAsync(TopicsChanged integrationEvent, CancellationToken cancellationToken)
    {
        var (owner, goal) = (integrationEvent.UserId, integrationEvent.LearningGoalId);
        var snapshot = await topics.SnapshotAsync(owner, goal, cancellationToken);
        if (snapshot is null)
        {
            topics.Add(TopicsSnapshot.Create(owner, goal, integrationEvent.ChangedAt));
        }
        else if (snapshot.IsOlderThan(integrationEvent.ChangedAt))
        {
            snapshot.MoveTo(integrationEvent.ChangedAt);
        }
        else
        {
            return;
        }

        var hidden = integrationEvent.HiddenTopicIds.ToHashSet();
        var current = integrationEvent.TopicIds.Concat(hidden).ToHashSet();
        var foundations = Foundations(integrationEvent, hidden);
        var known = await topics.ListAsync(owner, goal, current, cancellationToken);
        foreach (var topic in known)
        {
            if (current.Contains(topic.TopicId))
            {
                topic.Update(goal, hidden.Contains(topic.TopicId), foundations.Contains(topic.TopicId));
            }
            else if (topic.LearningGoalId == goal)
            {
                topics.Remove(topic);
            }
        }

        foreach (var id in current.Except(known.Select(topic => topic.TopicId)))
        {
            topics.Add(KnownTopic.Create(owner, id, goal, hidden.Contains(id), foundations.Contains(id)));
        }
    }

    // Active topics with at least one active subtopic (docs/spec/glossary.md#grundlage).
    private static HashSet<Guid> Foundations(TopicsChanged integrationEvent, HashSet<Guid> hidden)
    {
        var active = integrationEvent.TopicIds.Where(id => !hidden.Contains(id)).ToHashSet();
        return (integrationEvent.Parents ?? [])
            .Where(link => active.Contains(link.TopicId) && active.Contains(link.ParentId))
            .Select(link => link.ParentId)
            .ToHashSet();
    }
}