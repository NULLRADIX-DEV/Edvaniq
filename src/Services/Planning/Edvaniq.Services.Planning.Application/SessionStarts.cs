using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Application;

// Starts a session on a block of today's plan (#376). Topic, kind and minutes come from the plan, never from the
// caller. A user with an active session gets that one back, whichever block they asked for (docs/spec/acceptance.md
// A88), also when two starts come at once.
public sealed class SessionStarts(
    ISessions sessions, TodayPlans plans, ILearningGoals goals, ICurrentUser currentUser, TimeProvider time, ILogger<SessionStarts> logger)
{
    public async Task<SessionStart> StartAsync(Guid blockId, CancellationToken cancellationToken)
    {
        var start = await TryStartAsync(blockId, cancellationToken);
        switch (start.Result)
        {
            case SessionStartResult.Started:
                var session = start.Session!;
                PlanningLog.SessionStarted(logger, session.Id, session.BlockId, session.Kind == BlockKind.Review ? "review" : "learn", session.Minutes);
                break;
            case SessionStartResult.Existing:
                PlanningLog.SessionGoesOn(logger, start.Session!.Id);
                break;
            case SessionStartResult.BlockDone:
                PlanningLog.BlockDone(logger, blockId);
                break;
            default:
                PlanningLog.BlockGone(logger, blockId);
                break;
        }

        return start;
    }

    // A bonus review (#408) on the weakest topic, outside the plan, so whether the plan is done is up to the client
    // (nextStep "bonusReview"). Like a block, a user with an active session gets that one back.
    public async Task<SessionStart> StartBonusAsync(CancellationToken cancellationToken)
    {
        var start = await TryStartBonusAsync(cancellationToken);
        switch (start.Result)
        {
            case SessionStartResult.Started:
                PlanningLog.BonusStarted(logger, start.Session!.Id, start.Session.BlockId, start.Session.Minutes);
                break;
            case SessionStartResult.Existing:
                PlanningLog.SessionGoesOn(logger, start.Session!.Id);
                break;
            case SessionStartResult.MasteryUnavailable:
                PlanningLog.BonusWithoutMastery(logger);
                break;
            default:
                PlanningLog.BonusWithoutTopic(logger);
                break;
        }

        return start;
    }

    private async Task<SessionStart> TryStartBonusAsync(CancellationToken cancellationToken)
    {
        if (await sessions.ActiveAsync(cancellationToken) is { } active)
        {
            return new SessionStart(SessionStartResult.Existing, active);
        }

        // Without LearningEngine no topic is known to be introduced, so none would fit: that's no reason to send the user
        // away. Without Assessment every pool counts as ready, as in the plan.
        var today = await plans.TodayAsync(cancellationToken);
        if (!today.MasteryAvailable)
        {
            return new SessionStart(SessionStartResult.MasteryUnavailable);
        }

        if (today.BonusTopic is not { } topic)
        {
            return new SessionStart(SessionStartResult.NoBonusTopic);
        }

        // The session takes the goal of the plan its topic came from. The default goal must be in the database for it,
        // it may not be there yet.
        var goal = await goals.DefaultAsync(cancellationToken);
        if (goal.Id != today.LearningGoalId)
        {
            throw new InvalidOperationException("Today's plan is not of the default learning goal.");
        }

        var now = time.GetUtcNow();
        var session = Session.StartBonus(
            Guid.CreateVersion7(now), currentUser.Id, today.LearningGoalId, Guid.CreateVersion7(now), today.Plan.StudyDay, topic, now.UtcDateTime);
        if (await sessions.TryAddAsync(session, cancellationToken))
        {
            return new SessionStart(SessionStartResult.Started, session);
        }

        return await sessions.ActiveAsync(cancellationToken) is { } winner
            ? new SessionStart(SessionStartResult.Existing, winner)
            : throw new InvalidOperationException("A session start lost against an active session that is gone.");
    }

    private async Task<SessionStart> TryStartAsync(Guid blockId, CancellationToken cancellationToken)
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

        // The steps are fixed here (#377): a topic LearningEngine can't vouch for gets the introduction unit. A block
        // broken off before goes on from where it stopped, with the same steps (#393). Its id holds the day, so only a
        // session of today's block can match.
        var previous = await sessions.LastAbortedAsync(block.Id, cancellationToken);
        var session = Session.Start(
            Guid.CreateVersion7(now), currentUser.Id, goal.Id, today.Plan.StudyDay, block, now.UtcDateTime, today.IsNew(block.Block.TopicId), previous);
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

    // Bonus review only (#408): no introduced topic with a ready question pool.
    NoBonusTopic,

    // Bonus review only: LearningEngine couldn't be asked, so the weakest topic isn't known.
    MasteryUnavailable,
}