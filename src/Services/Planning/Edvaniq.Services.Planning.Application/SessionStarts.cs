using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Starts a session on a block of today's plan (#376). Topic, kind and minutes come from the plan, never from the
// caller. A user with an active session gets that one back, whichever block they asked for (docs/spec/acceptance.md
// A88), also when two starts come at once.
public sealed class SessionStarts(
    ISessions sessions, TodayPlans plans, ILearningGoals goals, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<SessionStart> StartAsync(Guid blockId, CancellationToken cancellationToken)
    {
        if (await sessions.ActiveAsync(cancellationToken) is { } active)
        {
            return new SessionStart(SessionStartResult.Existing, active);
        }

        // The block's id comes from the plan as it is worked out now. If the mastery or the question pools changed since
        // the plan was shown, an id may be gone: the caller loads the plan again (404), nothing is guessed.
        var today = await plans.TodayAsync(cancellationToken);
        if (today.Blocks.FirstOrDefault(block => block.Id == blockId) is not { } block)
        {
            return new SessionStart(SessionStartResult.BlockNotFound);
        }

        if (block.Done)
        {
            return new SessionStart(SessionStartResult.BlockDone);
        }

        // A session needs its goal in the database, the default goal may not be there yet.
        var goal = await goals.DefaultAsync(cancellationToken);
        var now = time.GetUtcNow();
        // The steps are fixed here (#377): a topic LearningEngine can't vouch for gets the introduction unit.
        var session = Session.Start(
            Guid.CreateVersion7(now), currentUser.Id, goal.Id, today.Plan.StudyDay, block, now.UtcDateTime, today.IsNew(block.Block.TopicId));
        if (await sessions.TryAddAsync(session, cancellationToken))
        {
            return new SessionStart(SessionStartResult.Started, session);
        }

        return await sessions.ActiveAsync(cancellationToken) is { } winner
            ? new SessionStart(SessionStartResult.Existing, winner)
            : throw new InvalidOperationException("A session start lost against an active session that is gone.");
    }
}

public sealed record SessionStart(SessionStartResult Result, Session? Session = null);

public enum SessionStartResult
{
    Started,

    // The user's active session, the one they had before.
    Existing,

    // Today's plan has no block with this id (any more).
    BlockNotFound,

    BlockDone,
}