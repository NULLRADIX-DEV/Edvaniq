using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.Application;

// One learning record per answerId (docs/spec/events.md#lern-nachweis). The inbox drops a redelivered message, this
// check the same answer under a new message id. If two arrive at once, the key makes the later save fail, the broker
// delivers it again and this check finds the record. A higher Version only adds optional fields, so it's read the same.
public sealed class AnswerRecordedHandler(ILearningRecords records) : IIntegrationEventHandler<AnswerRecorded>
{
    public async Task HandleAsync(AnswerRecorded integrationEvent, CancellationToken cancellationToken)
    {
        if (await records.ExistsAsync(integrationEvent.AnswerId, cancellationToken))
        {
            return;
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
}