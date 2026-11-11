using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// A topic of Knowledge that this service knows from TopicsChanged. Only the id and what the event says about it, no name
// and no order (rule 5). A topic belongs to exactly one learning goal (docs/spec/object-model.md), so the id is the key
// and the goal moves with it.
public sealed class KnownTopic : IOwnedByUser
{
    private KnownTopic(string ownerId, Guid topicId, Guid learningGoalId, bool hidden)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        LearningGoalId = learningGoalId;
        Hidden = hidden;
    }

    public Guid TopicId { get; private set; }

    public string OwnerId { get; private set; }

    public Guid LearningGoalId { get; private set; }

    public bool Hidden { get; private set; }

    public static KnownTopic Create(string ownerId, Guid topicId, Guid learningGoalId, bool hidden) =>
        new(ownerId, topicId, learningGoalId, hidden);

    public void Update(Guid learningGoalId, bool hidden)
    {
        LearningGoalId = learningGoalId;
        Hidden = hidden;
    }
}