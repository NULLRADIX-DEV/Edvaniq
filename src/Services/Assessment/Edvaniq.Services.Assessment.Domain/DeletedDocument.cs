using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Assessment.Domain;

// Content's document the user deleted, only its id. ItemsGenerated of the same document may still be on its way and
// arrive later on another consumer, this keeps it from bringing the questions back. No state of Content, no expiry: it
// goes with the user (UserDeleted).
public sealed class DeletedDocument : IOwnedByUser
{
    private DeletedDocument(string ownerId, Guid documentId, DateTime deletedAt)
    {
        OwnerId = ownerId;
        DocumentId = documentId;
        DeletedAt = deletedAt;
    }

    public Guid DocumentId { get; private set; }

    public string OwnerId { get; private set; }

    // UTC.
    public DateTime DeletedAt { get; private set; }

    public static DeletedDocument Of(string ownerId, Guid documentId, DateTime deletedAt) => new(ownerId, documentId, deletedAt);
}