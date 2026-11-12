using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// A topic of Knowledge that this service knows from TopicsChanged. Only the id and what this service works out from the
// event, no name and no parent id (rule 5). A topic belongs to exactly one learning goal (docs/spec/object-model.md), so
// the id is the key and the goal moves with it. IsFoundation: an active topic with at least one active subtopic
// (docs/spec/glossary.md#grundlage, #909). Rank and Size (#380): its place in the material, null while Knowledge hasn't
// said (messages from before), and how much there is to learn, the number of its sections and at least 1.
public sealed class KnownTopic : IOwnedByUser
{
    private KnownTopic(string ownerId, Guid topicId, Guid learningGoalId, bool hidden, bool isFoundation, int? rank, int size)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        LearningGoalId = learningGoalId;
        Hidden = hidden;
        IsFoundation = isFoundation;
        Rank = rank;
        Size = size;
    }

    public Guid TopicId { get; private set; }

    public string OwnerId { get; private set; }

    public Guid LearningGoalId { get; private set; }

    public bool Hidden { get; private set; }

    public bool IsFoundation { get; private set; }

    public int? Rank { get; private set; }

    public int Size { get; private set; } = 1;

    public static KnownTopic Create(
        string ownerId, Guid topicId, Guid learningGoalId, bool hidden, bool isFoundation = false, int? rank = null, int sections = 0) =>
        new(ownerId, topicId, learningGoalId, hidden, isFoundation, rank, SizeOf(sections));

    public void Update(Guid learningGoalId, bool hidden, bool isFoundation = false, int? rank = null, int sections = 0)
    {
        LearningGoalId = learningGoalId;
        Hidden = hidden;
        IsFoundation = isFoundation;
        Rank = rank;
        Size = SizeOf(sections);
    }

    // A topic without sections (one the user added, or a message from before) still has something to learn.
    private static int SizeOf(int sections) => Math.Max(sections, 1);
}