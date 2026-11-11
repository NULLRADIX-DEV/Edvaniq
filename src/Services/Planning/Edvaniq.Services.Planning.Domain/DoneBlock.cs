using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// A block whose session was finished (docs/spec/glossary.md#block). Only done blocks are stored: open ones are worked out
// anew on every call, so future days follow the current mastery (#375). StudyDay is the day the block was planned for,
// not the day it was finished: a session from 03:50 to 04:10 finishes yesterday's block. The block goes with its
// learning goal (foreign key with cascade) and with its user (IUserDataRemover).
public sealed class DoneBlock : IOwnedByUser
{
    private DoneBlock(Guid id, string ownerId, Guid learningGoalId, DateOnly studyDay, Guid topicId, BlockKind kind, int minutes, DateTime doneAt)
    {
        Id = id;
        OwnerId = ownerId;
        LearningGoalId = learningGoalId;
        StudyDay = studyDay;
        TopicId = topicId;
        Kind = kind;
        Minutes = minutes;
        DoneAt = doneAt;
    }

    // The block's id from the plan (BlockId), so the block that was done and the open one it was are the same.
    public Guid Id { get; private set; }

    public string OwnerId { get; private set; }

    public Guid LearningGoalId { get; private set; }

    public DateOnly StudyDay { get; private set; }

    public Guid TopicId { get; private set; }

    public BlockKind Kind { get; private set; }

    public int Minutes { get; private set; }

    // UTC.
    public DateTime DoneAt { get; private set; }

    public Block Block => new(TopicId, Minutes, Kind);

    public static DoneBlock Create(Guid id, string ownerId, Guid learningGoalId, DateOnly studyDay, Block block, DateTime doneAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(block.Minutes);
        return new DoneBlock(id, ownerId, learningGoalId, studyDay, block.TopicId, block.Kind, block.Minutes, doneAt);
    }
}