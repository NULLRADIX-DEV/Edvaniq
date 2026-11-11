using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Assessment.Application;

// Removes the questions of a deleted document (docs/spec/events.md) and every attempt on them, open or answered: an open
// one could not be answered without its question, an answered one holds options of a question that is gone. The
// learning records stay with LearningEngine. A second delivery finds the document deleted already and changes nothing.
public sealed class DocumentDeletedHandler(IDeletedDocuments documents, TimeProvider time) : IIntegrationEventHandler<DocumentDeleted>
{
    public async Task HandleAsync(DocumentDeleted integrationEvent, CancellationToken cancellationToken) =>
        await documents.RemoveAsync(integrationEvent.UserId, integrationEvent.DocumentId, time.GetUtcNow().UtcDateTime, cancellationToken);
}