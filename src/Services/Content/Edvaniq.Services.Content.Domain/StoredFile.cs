using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A file a user uploaded, as Content keeps it. The bytes live in the file store under the id, the row says whose they
// are. Nothing the user sends, not even the file's name, ever becomes part of where it's stored.
public sealed class StoredFile : IOwnedByUser
{
    private StoredFile(Guid id, string ownerId, long length, DateTime createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        Length = length;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string OwnerId { get; private set; }

    // In bytes.
    public long Length { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public static StoredFile Of(Guid id, string ownerId, long length, DateTime createdAt) => new(id, ownerId, length, createdAt);
}