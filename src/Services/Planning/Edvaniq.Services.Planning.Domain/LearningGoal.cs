using System.Security.Cryptography;
using System.Text;

using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// What a user learns towards (docs/spec/glossary.md#lernziel). Until the assistant comes in M2 every user has exactly one,
// the default goal, and it has no fields of its own yet. M2 adds name, kind and exam to this class, so the default goal
// becomes a real one under the same id and nothing that points at it has to change.
public sealed class LearningGoal : IOwnedByUser
{
    private LearningGoal(Guid id, string ownerId, DateTime createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string OwnerId { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public static LearningGoal CreateDefault(string ownerId, DateTime createdAt) =>
        new(DefaultIdFor(ownerId), ownerId, createdAt);

    // A name-based UUID (RFC 9562, version 8) from the user id, so two requests at once insert the same key and only one
    // of them gets through. Anyone who knows the user id can work it out, so it must never guard anything: who sees a
    // goal is up to the owner filter alone.
    public static Guid DefaultIdFor(string ownerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"edvaniq:default-learning-goal:{ownerId}"));
        var bytes = hash.AsSpan(0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}