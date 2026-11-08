using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Knowledge.Domain;

// A topic of a learning goal (docs/spec/glossary.md#thema). Other services keep nothing of it but its id, so the id never
// changes: not on a rename, and not when a topic later belongs to more than one learning goal. A subtopic is a topic
// with a parent, there is no third level.
public sealed class Topic : IOwnedByUser
{
    public const int NameMaxLength = 80;

    private Topic(string ownerId, Guid learningGoalId, Guid? parentId, string name, DateTime createdAt)
    {
        OwnerId = ownerId;
        LearningGoalId = learningGoalId;
        ParentId = parentId;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    // Planning's learning goal, only a reference. It's no part of the key, a second goal later leaves the id alone.
    public Guid LearningGoalId { get; private set; }

    // Null for a topic of the upper level.
    public Guid? ParentId { get; private set; }

    public string Name { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public bool IsSubtopic => ParentId is not null;

    // Why the name can't stay, or null if it can. The client shows the message as it is.
    public static string? CheckName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Der Name ist leer. Gib dem Thema einen Namen."
        : name.Trim().Length > NameMaxLength ? "Der Name ist zu lang. Erlaubt sind höchstens 80 Zeichen."
        : null;

    // A topic of the upper level. Call it only with a name that passed CheckName.
    public static Topic Create(string ownerId, Guid learningGoalId, string name, DateTime createdAt) =>
        new(ownerId, learningGoalId, null, name.Trim(), createdAt);

    // Belongs to the same user and learning goal as this topic. Call it only with a name that passed CheckName.
    public Topic AddSubtopic(string name, DateTime createdAt) =>
        IsSubtopic
            ? throw new InvalidOperationException("A subtopic has no subtopics, topics have two levels.")
            : new(OwnerId, LearningGoalId, Id, name.Trim(), createdAt);

    // Call it only with a name that passed CheckName.
    public void Rename(string name) => Name = name.Trim();
}