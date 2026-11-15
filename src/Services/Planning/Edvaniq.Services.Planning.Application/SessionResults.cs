using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Application;

// The result of an ended session of the current user (#403): the answers from Assessment, the mastery before and after
// from LearningEngine, both asked as queries with the caller's token (rule 4), and the daily goal and next block from
// today's plan as /plan/today shows it.
public sealed class SessionResults(
    ISessions sessions,
    ISessionAnswers answers,
    IMasteryChanges masteryChanges,
    TodayPlans plans,
    IKnownTopics knownTopics,
    ILogger<SessionResults> logger)
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

        // Gone from the goal since (#389, #406): LearningEngine won't name it any more, so no mastery would ever come. A
        // hidden topic is still known and gives a normal result, it tells what was.
        var topicRemoved = !(await knownTopics.ListAsync(session.OwnerId, session.LearningGoalId, [session.TopicId], cancellationToken))
            .Any(topic => topic.TopicId == session.TopicId);

        // The two queries touch no database, so they run while the plan reads this service's own. Both are awaited
        // together, so neither is left unobserved when the other fails.
        var learnt = LearntAsync(session.BlockId, chain, cancellationToken);
        var plan = plans.TodayAsync(cancellationToken);
        await Task.WhenAll(learnt, plan);
        var (result, masteryAvailable) = await learnt;
        var today = await plan;
        if (result is null)
        {
            PlanningLog.ResultWithoutAnswers(logger, session.Id);
            return new(SessionResultStatus.AnswersUnavailable, session);
        }

        if (!masteryAvailable)
        {
            PlanningLog.ResultWithoutMastery(logger, session.Id);
        }

        if (topicRemoved)
        {
            result = result with { Complete = false, Topics = null };
        }

        var next = today.Blocks.FirstOrDefault(block => block.Id == today.NextBlockId);
        return new(SessionResultStatus.Ready, session, result, masteryAvailable, today.DailyGoal, next, topicRemoved);
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
// TopicRemoved: the session's topic is no longer in the goal, its result has no mastery and won't get one.
public sealed record SessionResultLookup(
    SessionResultStatus Status,
    Session? Session = null,
    SessionResult? Result = null,
    bool MasteryAvailable = false,
    DailyGoalState DailyGoal = DailyGoalState.None,
    DayBlock? NextBlock = null,
    bool TopicRemoved = false);