using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.Planning.Contracts;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.LearningEngine.Application;

// Every topic a session introduced gets one introduction, the first one counts. A broken-off session still names the
// units finished in it (docs/spec/events.md), so Completed doesn't matter here. The inbox drops a redelivered message,
// this check the same session under a new message id. If two arrive at once, the key makes the later save fail, the
// broker delivers it again and this check finds the introduction. A topic not known yet keeps its introduction like an
// answer does (#205), a log line says so (#417).
public sealed class SessionCompletedHandler(ITopicIntroductions introductions, IKnownTopics knownTopics, ILogger<SessionCompletedHandler> logger)
    : IIntegrationEventHandler<SessionCompleted>
{
    public async Task HandleAsync(SessionCompleted integrationEvent, CancellationToken cancellationToken)
    {
        var topics = integrationEvent.IntroducedTopicIds.Distinct().ToList();
        if (topics.Count == 0)
        {
            return;
        }

        var introduced = await introductions.IntroducedAsync(integrationEvent.UserId, topics, cancellationToken);
        var added = topics.Where(topic => !introduced.Contains(topic)).ToList();
        foreach (var topic in added)
        {
            introductions.Add(TopicIntroduction.Create(
                integrationEvent.UserId, topic, integrationEvent.SessionId, integrationEvent.EndedAt, integrationEvent.UnitStartedAt));
        }

        if (added.Count == 0)
        {
            return;
        }

        LearningEngineLog.SessionIntroduced(logger, integrationEvent.SessionId, added.Count);
        var known = await knownTopics.KnownAsync(integrationEvent.UserId, added, cancellationToken);
        foreach (var topic in added.Where(topic => !known.Contains(topic)))
        {
            LearningEngineLog.IntroducedTopicNotKnownYet(logger, topic, integrationEvent.SessionId);
        }
    }
}