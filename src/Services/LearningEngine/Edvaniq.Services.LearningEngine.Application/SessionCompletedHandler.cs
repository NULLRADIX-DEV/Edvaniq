using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.Planning.Contracts;

namespace Edvaniq.Services.LearningEngine.Application;

// Every topic a session introduced gets one introduction, the first one counts. A broken-off session still names the
// units finished in it (docs/spec/events.md), so Completed doesn't matter here. The inbox drops a redelivered message,
// this check the same session under a new message id. If two arrive at once, the key makes the later save fail, the
// broker delivers it again and this check finds the introduction.
public sealed class SessionCompletedHandler(ITopicIntroductions introductions) : IIntegrationEventHandler<SessionCompleted>
{
    public async Task HandleAsync(SessionCompleted integrationEvent, CancellationToken cancellationToken)
    {
        var topics = integrationEvent.IntroducedTopicIds.Distinct().ToList();
        if (topics.Count == 0)
        {
            return;
        }

        var known = await introductions.IntroducedAsync(integrationEvent.UserId, topics, cancellationToken);
        foreach (var topic in topics.Where(topic => !known.Contains(topic)))
        {
            introductions.Add(TopicIntroduction.Create(
                integrationEvent.UserId, topic, integrationEvent.SessionId, integrationEvent.EndedAt));
        }
    }
}