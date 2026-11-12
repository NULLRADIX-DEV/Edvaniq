using System.Security.Cryptography;
using System.Text;

using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// What a user learns towards (docs/spec/glossary.md#lernziel). Every user has a default goal without fields, so Content
// has somewhere to put a text before the user created a goal. The first goal the user creates takes over the default
// goal's id, so everything that already points at it now belongs to that goal. The fields are null only on a default
// goal nobody has taken over yet.
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

    public LearningGoalKind? Kind { get; private set; }

    public string? Name { get; private set; }

    public ExamType? ExamType { get; private set; }

    public DateOnly? ExamDate { get; private set; }

    public LearningLevel? Level { get; private set; }

    public string? OutputLanguage { get; private set; }

    public string? Goal { get; private set; }

    // UTC, the last change by the user (#516), null until there was one. The replanning with a notice (#380, #524) goes by
    // it: a plan worked out before this time was made with other inputs.
    public DateTime? UpdatedAt { get; private set; }

    public static LearningGoal CreateDefault(string ownerId, DateTime createdAt) =>
        new(DefaultIdFor(ownerId), ownerId, createdAt);

    public static LearningGoal Create(Guid id, string ownerId, LearningGoalFields fields, DateTime createdAt)
    {
        var goal = new LearningGoal(id, ownerId, createdAt);
        goal.Kind = fields.Kind;
        goal.Name = fields.Name;
        goal.ExamType = fields.ExamType;
        goal.ExamDate = fields.ExamDate;
        goal.Level = fields.Level;
        goal.OutputLanguage = fields.OutputLanguage;
        goal.Goal = fields.Goal;
        return goal;
    }

    // Everything the user entered except the kind, which stays what it was (#516). Fields of another kind are a bug of
    // the caller: Parse them with this goal's kind.
    public void Update(LearningGoalFields fields, DateTime updatedAt)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (Kind is null)
        {
            throw new InvalidOperationException("A default goal nobody took over has nothing to change.");
        }

        if (fields.Kind != Kind)
        {
            throw new ArgumentException("The kind of a learning goal can't change.", nameof(fields));
        }

        Name = fields.Name;
        ExamType = fields.ExamType;
        ExamDate = fields.ExamDate;
        Level = fields.Level;
        OutputLanguage = fields.OutputLanguage;
        Goal = fields.Goal;

        // Whole microseconds, as the database keeps them, so the answer and a later read say the same.
        UpdatedAt = updatedAt.AddTicks(-(updatedAt.Ticks % 10));
    }

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