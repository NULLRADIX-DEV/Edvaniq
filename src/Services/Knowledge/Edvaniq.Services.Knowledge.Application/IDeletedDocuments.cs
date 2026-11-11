using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// Content's deleted documents. Runs in consumers without a current user, so every query names the owner. Infrastructure
// implements it.
public interface IDeletedDocuments
{
    Task<bool> IsDeletedAsync(string ownerId, Guid documentId, CancellationToken cancellationToken);

    // Notes the document as deleted and removes its sections and the topics that go with them (DocumentRemoval). Returns
    // the removed topics, none if the document was deleted before. Only changes the context, the consumer saves it.
    Task<IReadOnlyList<Topic>> RemoveAsync(string ownerId, Guid documentId, DateTime deletedAt, CancellationToken cancellationToken);
}