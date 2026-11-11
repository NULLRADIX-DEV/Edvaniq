using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Knowledge.Application;

// Takes over the topics of an analysis (docs/spec/events.md) and announces the new state of the learning goal with
// TopicsChanged in the same save. The inbox drops a redelivered message, and a second event for a document whose topics
// are here already changes nothing and announces nothing. Analysing a document again is #208.
public sealed class DocumentAnalyzedHandler(IAnalyzedTopics topics, TopicsChangedEvents changes, TimeProvider time)
    : IIntegrationEventHandler<DocumentAnalyzed>
{
    // DocumentAnalyzed carries no learning goal before the standard goal comes (#215). Until then the topics belong to
    // none and GET /topics without a goal lists them.
    public static readonly Guid NoLearningGoal = Guid.Empty;

    public async Task HandleAsync(DocumentAnalyzed integrationEvent, CancellationToken cancellationToken)
    {
        if (await topics.ExistForAsync(integrationEvent.UserId, integrationEvent.DocumentId, cancellationToken))
        {
            return;
        }

        var import = TopicImport.From(integrationEvent, NoLearningGoal, time.GetUtcNow().UtcDateTime);
        topics.Add(import);
        if (import.Topics.Count > 0)
        {
            await changes.AnnounceAsync(integrationEvent.UserId, NoLearningGoal, cancellationToken);
        }
    }
}