using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Knowledge.Domain;

// The topics of one learning goal of a user as a whole (#322). Every save that changes them counts Version up, and
// Version is a concurrency token: of two saves on the same goal at once, say two analyses or an analysis and a rename,
// only one goes through. The other one finds the topics as they are now when it tries again, so no name ends up twice
// and no change is lost. The row comes with the first change of the goal.
public sealed class TopicSet : IOwnedByUser
{
    private TopicSet(string ownerId, Guid learningGoalId, DateTime changedAt)
    {
        OwnerId = ownerId;
        LearningGoalId = learningGoalId;
        ChangedAt = changedAt;
    }

    public string OwnerId { get; private set; }

    public Guid LearningGoalId { get; private set; }

    public int Version { get; private set; } = 1;

    // UTC.
    public DateTime ChangedAt { get; private set; }

    public static TopicSet Of(string ownerId, Guid learningGoalId, DateTime changedAt) => new(ownerId, learningGoalId, changedAt);

    public void Touch(DateTime changedAt)
    {
        Version++;
        ChangedAt = changedAt;
    }
}