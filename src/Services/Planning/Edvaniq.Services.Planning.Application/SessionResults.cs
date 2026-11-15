using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The result of an ended session of the current user (#403): the answers from Assessment, the mastery before and after
// from LearningEngine, both asked as queries with the caller's token (rule 4), and the daily goal and next block from
// today's plan as /plan/today shows it.
public sealed class SessionResults(ISessions sessions, ISessionAnswers answers, IMasteryChanges masteryChanges, TodayPlans plans)
{
    public async Task<SessionResultLookup> OfAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await sessions.FindAsync(id, cancellationToken) is not { } session)
        {
            return new(SessionResultStatus.NotFound);
        }

        if (session.IsActive)
        {
            return new(SessionResultStatus.Running, session);
        }

        var chain = SessionResult.ChainOf(session, await sessions.OnBlockAsync(session.BlockId, cancellationToken));

        // The two queries touch no database, so they run while the plan reads this service's own.
        var learnt = LearntAsync(session.BlockId, chain, cancellationToken);
        var today = await plans.TodayAsync(cancellationToken);
        var (result, masteryAvailable) = await learnt;
        if (result is null)
        {
            return new(SessionResultStatus.AnswersUnavailable, session);
        }

        var next = today.Blocks.FirstOrDefault(block => block.Id == today.NextBlockId);
        return new(SessionResultStatus.Ready, session, result, masteryAvailable, today.DailyGoal, next);
    }

    private async Task<(SessionResult? Result, bool MasteryAvailable)> LearntAsync(
        Guid blockId, IReadOnlyList<Session> chain, CancellationToken cancellationToken)
    {
        if (await answers.OfBlockAsync(blockId, [.. chain.Select(session => session.Id)], cancellationToken) is not { } given)
        {
            return (null, false);
        }

        var changes = await masteryChanges.BetweenAsync(
            SessionResult.TopicsOf(chain, given), SessionResult.WindowOf(chain, given), [.. given.Select(answer => answer.AnswerId)], cancellationToken);
        return (SessionResult.Of(chain, given, changes), changes is not null);
    }
}

public enum SessionResultStatus
{
    Ready,
    NotFound,

    // The session runs, there is no result yet.
    Running,

    // Assessment couldn't be asked: without the answers there is nothing to show.
    AnswersUnavailable,
}

// NextBlock: the block "Jetzt lernen" goes on with, null when nothing is open (then a bonus review comes, #408).
public sealed record SessionResultLookup(
    SessionResultStatus Status,
    Session? Session = null,
    SessionResult? Result = null,
    bool MasteryAvailable = false,
    DailyGoalState DailyGoal = DailyGoalState.None,
    DayBlock? NextBlock = null);