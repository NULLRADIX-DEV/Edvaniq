using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Knowledge.Application;

// Removes what came from a deleted document (docs/spec/events.md): its sections, and the topics left without any. Every
// learning goal that lost a topic gets one TopicsChanged with its new state in the same save. A second delivery finds
// the document deleted already and changes nothing.
public sealed class DocumentDeletedHandler(IDeletedDocuments documents, TopicsChangedEvents changes, TimeProvider time)
    : IIntegrationEventHandler<DocumentDeleted>
{
    public async Task HandleAsync(DocumentDeleted integrationEvent, CancellationToken cancellationToken)
    {
        var removed = await documents.RemoveAsync(
            integrationEvent.UserId, integrationEvent.DocumentId, time.GetUtcNow().UtcDateTime, cancellationToken);
        foreach (var goal in removed.Select(topic => topic.LearningGoalId).Distinct())
        {
            await changes.AnnounceAsync(integrationEvent.UserId, goal, cancellationToken);
        }
    }
}