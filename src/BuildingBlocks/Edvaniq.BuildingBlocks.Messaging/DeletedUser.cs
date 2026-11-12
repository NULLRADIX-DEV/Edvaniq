using System.Security.Cryptography;
using System.Text;

namespace Edvaniq.BuildingBlocks.Messaging;

// A user this service has deleted. Only a hash of the id, and only for MessagingOptions.DeletedUserRetention: long enough
// for any late event, then the cleanup drops it. The id is a random GUID, so the hash can't be turned back into it, it
// only matches the same id from somewhere else.
public sealed class DeletedUser
{
    public const int HashLength = 64;

    private const string Prefix = "edvaniq-deleted-user:";

    private DeletedUser(string hash, DateTime deletedAt)
    {
        Hash = hash;
        DeletedAt = deletedAt;
    }

    public string Hash { get; private set; }

    // UTC.
    public DateTime DeletedAt { get; private set; }

    // Set once the sweep looked for rows a handler stored while the deletion ran.
    public bool Swept { get; private set; }

    public static DeletedUser Of(string userId, DateTime deletedAt) => new(HashOf(userId), deletedAt);

    // Case matters, like for the OwnerId.
    public static string HashOf(string userId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Prefix + userId)));

    public void MarkSwept() => Swept = true;
}