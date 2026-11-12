using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Knowledge.Domain;

// A topic of a learning goal (docs/spec/glossary.md#thema). Other services keep nothing of it but its id, so the id never
// changes: not on a rename, not when a later analysis brings it again, and not when a topic later belongs to more than
// one learning goal. A subtopic is a topic with a parent, there is no third level.
public sealed class Topic : IOwnedByUser
{
    public const int NameMaxLength = 80;

    private Topic(string ownerId, Guid learningGoalId, Guid? parentId, string name, DateTime createdAt, Guid? documentId, int position)
    {
        OwnerId = ownerId;
        LearningGoalId = learningGoalId;
        ParentId = parentId;
        Name = name;
        CreatedAt = createdAt;
        DocumentId = documentId;
        Position = position;
        AnalysisName = documentId is null ? null : name;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    // Planning's learning goal, only a reference. It's no part of the key, a second goal later leaves the id alone.
    public Guid LearningGoalId { get; private set; }

    // Null for a topic of the upper level.
    public Guid? ParentId { get; private set; }

    public string Name { get; private set; }

    // The name the analysis gave it first. A rename leaves it, so a later analysis with that name still finds the topic.
    // Null for a topic the user adds.
    public string? AnalysisName { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // Content's document whose analysis brought the topic first, only a reference. Null for a topic the user adds.
    public Guid? DocumentId { get; private set; }

    // The place in the analysis, keeps its order among topics created at the same time.
    public int Position { get; private set; }

    // Hidden by the user, Show brings it back. Only the topic's own state: a subtopic of a hidden topic is hidden too,
    // whatever it says here, and comes back in its own state when the topic does (IsHiddenUnder).
    public bool Hidden { get; private set; }

    public bool IsSubtopic => ParentId is not null;

    // Why the name can't stay, or null if it can. The client shows the message as it is.
    public static string? CheckName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Der Name ist leer. Gib dem Thema einen Namen."
        : name.Trim().Length > NameMaxLength ? "Der Name ist zu lang. Erlaubt sind höchstens 80 Zeichen."
        : null;

    // A topic of the upper level. Call it only with a name that passed CheckName.
    public static Topic Create(string ownerId, Guid learningGoalId, string name, DateTime createdAt, Guid? documentId = null, int position = 0) =>
        new(ownerId, learningGoalId, null, name.Trim(), createdAt, documentId, position);

    // Belongs to the same user and learning goal as this topic. Call it only with a name that passed CheckName.
    public Topic AddSubtopic(string name, DateTime createdAt, Guid? documentId = null, int position = 0) =>
        IsSubtopic
            ? throw new InvalidOperationException("A subtopic has no subtopics, topics have two levels.")
            : new(OwnerId, LearningGoalId, Id, name.Trim(), createdAt, documentId, position);

    // Whether a name means this topic, its name now or the one the analysis gave it, compared after TopicName.Normalize.
    public bool IsCalled(string name)
    {
        var normalized = TopicName.Normalize(name);
        return TopicName.Normalize(Name) == normalized
            || (AnalysisName is not null && TopicName.Normalize(AnalysisName) == normalized);
    }

    // Content's section of the document belongs to this topic.
    public TopicSection AssignSection(Guid documentId, Guid sectionId) => new(OwnerId, Id, documentId, sectionId);

    // A key term of this topic from the section of the document (#292).
    public KeyTerm KeyTerm(Guid documentId, Guid sectionId, string term, string definition, DateTime createdAt, int position) =>
        Domain.KeyTerm.Of(this, documentId, sectionId, term, definition, createdAt, position);

    // Call it only with a name that passed CheckName.
    public void Rename(string name) => Name = name.Trim();

    public void Hide() => Hidden = true;

    public void Show() => Hidden = false;

    // Whether the user sees the topic: its own state, and for a subtopic also that of parent, its topic.
    public bool IsHiddenUnder(Topic? parent) => Hidden || (parent is { Hidden: true } && parent.Id == ParentId);

    // The topic goes to another learning goal and keeps its id, so whatever points to it stays valid.
    public void MoveTo(Guid learningGoalId) => LearningGoalId = learningGoalId;
}