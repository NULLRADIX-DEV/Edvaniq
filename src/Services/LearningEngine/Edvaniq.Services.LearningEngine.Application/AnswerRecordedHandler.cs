using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.LearningEngine.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.LearningEngine.Application;

// One learning record per answerId (docs/spec/events.md#lern-nachweis). The inbox drops a redelivered message, this
// check the same answer under a new message id. If two arrive at once, the key makes the later save fail, the broker
// delivers it again and this check finds the record. A higher Version only adds optional fields, so it's read the same.
// An answer can come before TopicsChanged names its topic (#205). It's stored all the same and counts once the topic is
// known, mastery is worked out from the records then, so the result is the same as in order. A log line says so.
public sealed partial class AnswerRecordedHandler(ILearningRecords records, ILogger<AnswerRecordedHandler> logger)
    : IIntegrationEventHandler<AnswerRecorded>
{
    public async Task HandleAsync(AnswerRecorded integrationEvent, CancellationToken cancellationToken)
    {
        if (await records.ExistsAsync(integrationEvent.AnswerId, cancellationToken))
        {
            return;
        }

        if (!await records.IsTopicKnownAsync(integrationEvent.UserId, integrationEvent.TopicId, cancellationToken))
        {
            LogTopicNotKnownYet(integrationEvent.AnswerId, integrationEvent.TopicId);
        }

        records.Add(LearningRecord.Create(
            integrationEvent.AnswerId,
            integrationEvent.UserId,
            integrationEvent.TopicId,
            integrationEvent.ItemId,
            integrationEvent.SessionId,
            integrationEvent.Correct,
            integrationEvent.Difficulty,
            integrationEvent.ResponseTimeMs,
            integrationEvent.Confidence,
            (Domain.Mode)integrationEvent.Source,
            integrationEvent.AnsweredAt));
    }

    // Ids only, never the user or the answer.
    [LoggerMessage(Level = LogLevel.Information, Message = "Answer {AnswerId} is kept for topic {TopicId}, which isn't known yet; it counts once the topic is")]
    private partial void LogTopicNotKnownYet(Guid answerId, Guid topicId);
}