using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// The demo of a user (#414), one per user: its row is stored with the demo documents, so a second request at the same
// time fails on the key instead of pasting the material twice. It goes with the documents.
public sealed class DemoGoal : IOwnedByUser
{
    private DemoGoal(string ownerId, Guid learningGoalId, DateTime createdAt)
    {
        OwnerId = ownerId;
        LearningGoalId = learningGoalId;
        CreatedAt = createdAt;
    }

    public string OwnerId { get; private set; }

    // Planning's goal the client made for the demo.
    public Guid LearningGoalId { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public static DemoGoal Of(string ownerId, Guid learningGoalId, DateTime createdAt) => new(ownerId, learningGoalId, createdAt);
}