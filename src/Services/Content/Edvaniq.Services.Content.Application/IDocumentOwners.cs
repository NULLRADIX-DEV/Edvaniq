namespace Edvaniq.Services.Content.Application;

// Whose document this is, past the owner filter. Only for the operator's document delete command, which then deletes
// as that user. Nothing in a request may use it.
public interface IDocumentOwners
{
    // Null when there is no document with this id.
    Task<string?> OwnerOfAsync(Guid documentId, CancellationToken cancellationToken);
}