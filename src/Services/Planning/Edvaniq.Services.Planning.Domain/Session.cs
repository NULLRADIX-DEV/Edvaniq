using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Planning.Domain;

// The run through one block of the plan (docs/spec/glossary.md#session). Bound to the block and its topic from the
// moment it starts. A user has at most one active session: ActiveOwnerId is the owner while it runs and null once it
// ended, and the database keeps it unique, so even two starts at once leave one. Broken off it ends as Aborted (#393),
// finishing it comes with #395.
public sealed class Session : IOwnedByUser
{
    private Session(
        Guid id, string ownerId, Guid learningGoalId, Guid blockId, DateOnly studyDay, Guid topicId, BlockKind kind, int minutes, bool topicIsNew, int currentStep, DateTime startedAt)
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
        TopicIsNew = topicIsNew;
        CurrentStep = currentStep;
        StartedAt = startedAt;
        LastActivityAt = startedAt;
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

    // Whether the topic was new when the session started (#377). Kept, so the steps stay as they were even after the
    // topic got introduced in this session or LearningEngine answers differently.
    public bool TopicIsNew { get; private set; }

    public IReadOnlyList<SessionStep> Steps => SessionSteps.For(Kind, TopicIsNew);

    // UTC.
    public DateTime StartedAt { get; private set; }

    // UTC, null while it runs.
    public DateTime? EndedAt { get; private set; }

    public bool IsActive => EndedAt is null;

    public SessionOutcome Outcome { get; private set; } = SessionOutcome.Running;

    // The index in Steps the user is at (#393). Only ever moves forward, and a session started again on a block takes it
    // over from the one broken off, so it goes on where that one stopped.
    public int CurrentStep { get; private set; }

    // Active learning time so far (ActiveTime, #394), counted at every signal: the start, moving on, breaking off and the
    // activity the client reports while the user works on a step.
    // Kept in milliseconds, whole seconds would lose up to one at every signal.
    public long ActiveMilliseconds { get; private set; }

    public int ActiveSeconds => (int)(ActiveMilliseconds / 1000);

    // UTC, the last signal. Also the concurrency token, so of two signals at once only one counts the gap.
    public DateTime LastActivityAt { get; private set; }

    // previous: the session broken off last on this block. The new one goes on from its step with its steps, even if the
    // topic got introduced since.
    public static Session Start(
        Guid id, string ownerId, Guid learningGoalId, DateOnly studyDay, DayBlock block, DateTime startedAt, bool topicIsNew = false, Session? previous = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(block);
        if (block.Done)
        {
            throw new InvalidOperationException("A block that is done can't be started again.");
        }

        if (previous is not null && (previous.BlockId != block.Id || previous.Outcome != SessionOutcome.Aborted))
        {
            throw new ArgumentException("Only a session broken off on the same block can be gone on from.", nameof(previous));
        }

        return new Session(
            id,
            ownerId,
            learningGoalId,
            block.Id,
            studyDay,
            block.Block.TopicId,
            block.Block.Kind,
            block.Block.Minutes,
            previous?.TopicIsNew ?? topicIsNew,
            previous?.CurrentStep ?? 0,
            Stored(startedAt));
    }

    // Ends the session as broken off. False when it ended already, then nothing changes.
    public bool Abort(DateTime endedAt)
    {
        if (!IsActive)
        {
            return false;
        }

        Count(endedAt);
        EndedAt = Stored(endedAt);
        ActiveOwnerId = null;
        Outcome = SessionOutcome.Aborted;
        return true;
    }

    // Moves to the step, never back: a step at or before the current one changes nothing, so a repeated call is harmless.
    public SessionAdvance Advance(int step, DateTime at)
    {
        if (!IsActive)
        {
            return SessionAdvance.Ended;
        }

        if (step < 0 || step >= Steps.Count)
        {
            return SessionAdvance.NoSuchStep;
        }

        // Any call with a real step on a running session is a signal of the user (#394).
        Count(at);
        if (step <= CurrentStep)
        {
            return SessionAdvance.Unchanged;
        }

        CurrentStep = step;
        return SessionAdvance.Advanced;
    }

    // The user is working on the session. False once it ended: time after the end never counts.
    public bool RecordActivity(DateTime at)
    {
        if (!IsActive)
        {
            return false;
        }

        Count(at);
        return true;
    }

    private void Count(DateTime at)
    {
        var stored = Stored(at);
        ActiveMilliseconds += (long)ActiveTime.Between(LastActivityAt, stored).TotalMilliseconds;
        if (stored > LastActivityAt)
        {
            LastActivityAt = stored;
        }
    }

    // Whole microseconds, as the database keeps them, so the session read back is the one stored.
    private static DateTime Stored(DateTime value) => value.AddTicks(-(value.Ticks % 10));
}

public enum SessionOutcome
{
    Running,

    // Broken off (#393): the answers count, the block stays open and goes on from here.
    Aborted,
}

public enum SessionAdvance
{
    Advanced,
    Unchanged,
    NoSuchStep,
    Ended,
}