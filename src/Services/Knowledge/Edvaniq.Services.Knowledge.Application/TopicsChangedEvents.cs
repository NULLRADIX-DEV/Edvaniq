using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Knowledge.Application;

// Every save that changes the topics of a learning goal calls this once, before it saves: TopicsChanged then goes out
// with the change or not at all.
public sealed class TopicsChangedEvents(ITopicState state, IEventOutbox outbox, TimeProvider time)
{
    public async Task AnnounceAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken)
    {
        var topicIds = await state.TopicIdsAsync(ownerId, learningGoalId, cancellationToken);
        outbox.Add(new TopicsChanged(ownerId, learningGoalId, Now(), topicIds, []));
    }

    // Whole microseconds, what a datetime(6) column keeps. A receiver compares the value it stored with the next one.
    private DateTime Now()
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - (now.Ticks % 10), DateTimeKind.Utc);
    }
}