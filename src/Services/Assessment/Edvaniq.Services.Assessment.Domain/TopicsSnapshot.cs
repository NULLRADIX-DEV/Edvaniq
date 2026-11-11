using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Assessment.Domain;

// Which state of a learning goal's topics was taken over last. A TopicsChanged that isn't newer changes nothing, so a
// second delivery and an older state that arrives late both leave the known topics alone.
public sealed class TopicsSnapshot : IOwnedByUser
{
    private TopicsSnapshot(string ownerId, Guid learningGoalId, DateTime changedAt)
    {
        OwnerId = ownerId;
        LearningGoalId = learningGoalId;
        ChangedAt = changedAt;
    }

    public string OwnerId { get; private set; }

    public Guid LearningGoalId { get; private set; }

    // UTC, whole microseconds like the column.
    public DateTime ChangedAt { get; private set; }

    public static TopicsSnapshot Create(string ownerId, Guid learningGoalId, DateTime changedAt) =>
        new(ownerId, learningGoalId, Microseconds(changedAt));

    public bool IsOlderThan(DateTime changedAt) => Microseconds(changedAt) > ChangedAt;

    public void MoveTo(DateTime changedAt) => ChangedAt = Microseconds(changedAt);

    private static DateTime Microseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);
}