namespace Edvaniq.Services.Assessment.Application;

// Content's deleted documents. Runs in consumers without a current user, so every query names the owner. Infrastructure
// implements it.
public interface IDeletedDocuments
{
    Task<bool> IsDeletedAsync(string ownerId, Guid documentId, CancellationToken cancellationToken);

    // Notes the document as deleted and removes its questions with every attempt on them. False if it was deleted before.
    // Only changes the context, the consumer saves it.
    Task<bool> RemoveAsync(string ownerId, Guid documentId, DateTime deletedAt, CancellationToken cancellationToken);
}