using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// A topic of Knowledge that this service knows from TopicsChanged. Only the id and what this service works out from the
// event, no name, no order and no parent id (rule 5). A topic belongs to exactly one learning goal
// (docs/spec/object-model.md), so the id is the key and the goal moves with it. IsFoundation: an active topic with at
// least one active subtopic (docs/spec/glossary.md#grundlage, #909).
public sealed class KnownTopic : IOwnedByUser
{
    private KnownTopic(string ownerId, Guid topicId, Guid learningGoalId, bool hidden, bool isFoundation)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        LearningGoalId = learningGoalId;
        Hidden = hidden;
        IsFoundation = isFoundation;
    }

    public Guid TopicId { get; private set; }

    public string OwnerId { get; private set; }

    public Guid LearningGoalId { get; private set; }

    public bool Hidden { get; private set; }

    public bool IsFoundation { get; private set; }

    public static KnownTopic Create(string ownerId, Guid topicId, Guid learningGoalId, bool hidden, bool isFoundation = false) =>
        new(ownerId, topicId, learningGoalId, hidden, isFoundation);

    public void Update(Guid learningGoalId, bool hidden, bool isFoundation = false)
    {
        LearningGoalId = learningGoalId;
        Hidden = hidden;
        IsFoundation = isFoundation;
    }
}