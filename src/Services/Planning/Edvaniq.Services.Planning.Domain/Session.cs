using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// The run through one block of the plan (docs/spec/glossary.md#session). Bound to the block and its topic from the
// moment it starts. A user has at most one active session: ActiveOwnerId is the owner while it runs and null once it
// ended, and the database keeps it unique, so even two starts at once leave one. Ending it comes with #393/#395.
public sealed class Session : IOwnedByUser
{
    private Session(
        Guid id, string ownerId, Guid learningGoalId, Guid blockId, DateOnly studyDay, Guid topicId, BlockKind kind, int minutes, DateTime startedAt)
    {
        Id = id;
        OwnerId = ownerId;
        ActiveOwnerId = ownerId;
        LearningGoalId = learningGoalId;
        BlockId = blockId;
        StudyDay = studyDay;
        TopicId = topicId;
        Kind = kind;
        Minutes = minutes;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }

    public string OwnerId { get; private set; }

    // The owner while the session runs, null once it ended.
    public string? ActiveOwnerId { get; private set; }

    public Guid LearningGoalId { get; private set; }

    // The block of the plan (DayBlocks), with the day it was planned for.
    public Guid BlockId { get; private set; }

    public DateOnly StudyDay { get; private set; }

    public Guid TopicId { get; private set; }

    public BlockKind Kind { get; private set; }

    public int Minutes { get; private set; }

    // UTC.
    public DateTime StartedAt { get; private set; }

    // UTC, null while it runs.
    public DateTime? EndedAt { get; private set; }

    public bool IsActive => EndedAt is null;

    public static Session Start(Guid id, string ownerId, Guid learningGoalId, DateOnly studyDay, DayBlock block, DateTime startedAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(block);
        if (block.Done)
        {
            throw new InvalidOperationException("A block that is done can't be started again.");
        }

        // Whole microseconds, as the database keeps them, so the session read back is the one started.
        var stored = startedAt.AddTicks(-(startedAt.Ticks % 10));
        return new Session(
            id, ownerId, learningGoalId, block.Id, studyDay, block.Block.TopicId, block.Block.Kind, block.Block.Minutes, stored);
    }
}