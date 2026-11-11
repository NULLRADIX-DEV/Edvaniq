using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Assessment.Application;

// Takes over the state of a learning goal's topics (docs/spec/events.md): adds the new ones, drops those in neither
// list. The inbox drops a redelivered message, the snapshot the same state under a new message id and an older state
// that arrives after a newer one. If two arrive at once, the key or the snapshot's token makes the later save fail and
// the broker delivers it again.
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
        var known = await topics.ListAsync(owner, goal, current, cancellationToken);
        foreach (var topic in known)
        {
            if (current.Contains(topic.TopicId))
            {
                topic.Update(goal, hidden.Contains(topic.TopicId));
            }
            else if (topic.LearningGoalId == goal)
            {
                topics.Remove(topic);
            }
        }

        foreach (var id in current.Except(known.Select(topic => topic.TopicId)))
        {
            topics.Add(KnownTopic.Create(owner, id, goal, hidden.Contains(id)));
        }
    }
}