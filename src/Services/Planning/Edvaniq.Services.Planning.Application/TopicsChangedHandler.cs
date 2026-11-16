using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Application;

// Takes over the state of a learning goal's topics (docs/spec/events.md): adds the new ones, drops those in neither
// list. The inbox drops a redelivered message, the snapshot the same state under a new message id and an older state
// that arrives after a newer one. If two arrive at once, the key or the snapshot's token makes the later save fail and
// the broker delivers it again. Of the parents it keeps only which active topic has an active subtopic (#909, rule 5), of
// the facts only rank and size (#380). A topic that leaves the goal takes its done blocks and a session running on it
// along (#412), so today's plan is worked out afresh. Ended sessions stay, their result tells the topic is gone (#406).
// All of it goes in the inbox's one save: if the session changed at the same time, nothing is stored and the broker
// delivers the state again.
public sealed class TopicsChangedHandler(
    IKnownTopics topics, ITopicCleanup cleanup, IEventOutbox outbox, TimeProvider time, ILogger<TopicsChangedHandler> logger)
    : IIntegrationEventHandler<TopicsChanged>
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
        var facts = (integrationEvent.Topics ?? []).DistinctBy(item => item.TopicId).ToDictionary(item => item.TopicId);
        var known = await topics.ListAsync(owner, goal, current, cancellationToken);
        var removed = new HashSet<Guid>();
        foreach (var topic in known)
        {
            if (current.Contains(topic.TopicId))
            {
                var fact = facts.GetValueOrDefault(topic.TopicId);
                topic.Update(goal, hidden.Contains(topic.TopicId), foundations.Contains(topic.TopicId), fact?.Rank, fact?.Sections ?? 0);
            }
            else if (topic.LearningGoalId == goal)
            {
                topics.Remove(topic);
                removed.Add(topic.TopicId);
            }
        }

        foreach (var id in current.Except(known.Select(topic => topic.TopicId)))
        {
            var fact = facts.GetValueOrDefault(id);
            topics.Add(KnownTopic.Create(owner, id, goal, hidden.Contains(id), foundations.Contains(id), fact?.Rank, fact?.Sections ?? 0));
        }

        if (removed.Count > 0)
        {
            await ClearUpAsync(owner, goal, removed, cancellationToken);
        }
    }

    // A hidden topic stays known and keeps all of it, only one that is gone from the goal is cleared up.
    private async Task ClearUpAsync(string owner, Guid goal, HashSet<Guid> removed, CancellationToken cancellationToken)
    {
        var blocks = await cleanup.RemoveDoneBlocksAsync(owner, goal, removed, cancellationToken);
        PlanningLog.DoneBlocksRemoved(logger, removed.Count, goal, blocks);

        if (await cleanup.ActiveSessionAsync(owner, cancellationToken) is not { } session
            || session.LearningGoalId != goal
            || !removed.Contains(session.TopicId))
        {
            return;
        }

        // Read before the change, as SessionChanges does.
        var unitStartedAt = session.UnitStartedAt(await cleanup.OnBlockAsync(owner, session.BlockId, cancellationToken));
        if (session.Abort(time.GetUtcNow().UtcDateTime))
        {
            outbox.Add(SessionEnds.EventOf(session, unitStartedAt));
            PlanningLog.SessionAbortedTopicRemoved(logger, session.Id, session.TopicId, goal);
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