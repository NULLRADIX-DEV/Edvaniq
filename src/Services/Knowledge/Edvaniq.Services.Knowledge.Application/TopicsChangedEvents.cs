using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Knowledge.Application;

// Every save that changes the topics of a learning goal calls this once, before it saves: TopicsChanged then goes out
// with the change or not at all. Every subtopic comes with its parent (#909).
public sealed class TopicsChangedEvents(ITopicState state, IEventOutbox outbox, TimeProvider time)
{
    public async Task AnnounceAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken)
    {
        var topics = await state.TopicsAsync(ownerId, learningGoalId, cancellationToken);
        var parents = topics
            .Where(topic => topic.ParentId is not null)
            .Select(topic => new TopicParent(topic.Id, topic.ParentId!.Value))
            .ToList();
        outbox.Add(new TopicsChanged(ownerId, learningGoalId, Now(), [.. topics.Select(topic => topic.Id)], [], parents));
    }

    // Whole microseconds, what a datetime(6) column keeps. A receiver compares the value it stored with the next one.
    private DateTime Now()
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - (now.Ticks % 10), DateTimeKind.Utc);
    }
}