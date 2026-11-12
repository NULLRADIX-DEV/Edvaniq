using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Today's plan of the current user for the default learning goal. Topics come from this service's own database, mastery
// and state from one query to LearningEngine, the ready question pools from one query to Assessment (#374). The two run
// side by side, so a plan costs at most two calls to other services at once. Either one missing still gives a plan,
// only a restricted one, and the result says which (docs/spec/acceptance.md A92).
public sealed class TodayPlans(
    IKnownTopics knownTopics,
    IMasterySource masteries,
    IQuestionPoolSource questionPools,
    IDailyBudgets budgets,
    IDoneBlocks doneBlocks,
    IStartedBlocks startedBlocks,
    ICurrentUser currentUser,
    TimeProvider time)
{
    public async Task<TodayPlanResult> TodayAsync(CancellationToken cancellationToken) =>
        (await WeekAsync(cancellationToken)).Today;

    // The rolling week (#379). Today is exactly TodayAsync, the later days are the fresh plan with ids, nothing is done
    // there yet.
    public async Task<WeekPlanResult> WeekAsync(CancellationToken cancellationToken)
    {
        var owner = currentUser.Id;
        var goal = LearningGoal.DefaultIdFor(owner);

        var topics = await knownTopics.ActiveAsync(owner, goal, cancellationToken);
        IReadOnlyDictionary<Guid, TopicStanding>? mastery = null;
        IReadOnlySet<Guid>? ready = null;
        if (topics.Count > 0)
        {
            // No shared cancellation: one failing never stops the other.
            var masteryCall = OrNullAsync(() => masteries.OfLearningGoalAsync(goal, cancellationToken), cancellationToken);
            var poolsCall = OrNullAsync(() => questionPools.ReadyTopicsAsync(goal, cancellationToken), cancellationToken);
            await Task.WhenAll(masteryCall, poolsCall);
            (mastery, ready) = (await masteryCall, await poolsCall);
        }

        var budget = await budgets.ForUserAsync(owner, cancellationToken);
        var foundations = await knownTopics.FoundationsAsync(owner, goal, cancellationToken);
        var sizes = await knownTopics.SizesAsync(owner, goal, cancellationToken);

        var week = TodayPlanner.Week(
            topics,
            mastery ?? new Dictionary<Guid, TopicStanding>(),
            budget,
            time.GetUtcNow().UtcDateTime,
            readyTopics: ready,
            foundations: foundations,
            sizes: sizes);
        var plan = week.Today;

        // What was done today stays, the rest comes from the fresh plan (#375).
        var done = await doneBlocks.OnAsync(goal, plan.StudyDay, cancellationToken);
        var blocks = DayBlocks.WithDone(owner, goal, plan.StudyDay, plan.Blocks, done, budget.MinutesOn(plan.StudyDay));

        // A block started already goes on first (#407).
        var started = await startedBlocks.OnAsync(goal, plan.StudyDay, cancellationToken);
        var today = new TodayPlanResult(
            goal,
            plan,
            blocks,
            MasteryAvailable: topics.Count == 0 || mastery is not null,
            QuestionPoolsAvailable: topics.Count == 0 || ready is not null,
            NextBlockId: NextBlock.Of(blocks, started)?.Id)
        {
            Standings = mastery ?? new Dictionary<Guid, TopicStanding>(),
        };

        List<WeekDayBlocks> days =
        [
            new(plan.StudyDay, blocks),
            .. week.Days.Skip(1).Select(day => new WeekDayBlocks(
                day.StudyDay, DayBlocks.WithDone(owner, goal, day.StudyDay, day.Blocks, [], budget.MinutesOn(day.StudyDay)))),
        ];
        return new WeekPlanResult(today, week, days);
    }

    // The sources answer null when their service is down. Should one throw anyway, it counts the same, unless the
    // request itself was cancelled.
    private static async Task<T?> OrNullAsync<T>(Func<Task<T?>> call, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

// Today: what TodayAsync answers. Days: the blocks with ids of every day of the week, Days[0] are Today.Blocks.
public sealed record WeekPlanResult(TodayPlanResult Today, WeekPlan Plan, IReadOnlyList<WeekDayBlocks> Days);

public sealed record WeekDayBlocks(DateOnly StudyDay, IReadOnlyList<DayBlock> Blocks);

// Blocks: today's blocks with their ids, the done ones first. Plan.Blocks is only the fresh part.
// NextBlockId: the block "Jetzt lernen" goes on with, null when nothing is open (#407). Standings: what LearningEngine
// answered, empty when it couldn't be asked.
public sealed record TodayPlanResult(
    Guid LearningGoalId,
    TodayPlan Plan,
    IReadOnlyList<DayBlock> Blocks,
    bool MasteryAvailable,
    bool QuestionPoolsAvailable,
    Guid? NextBlockId)
{
    // #381: judged on Blocks, so on what the plan shows.
    public DailyGoalState DailyGoal => Domain.DailyGoal.Of(Blocks);

    public IReadOnlyDictionary<Guid, TopicStanding> Standings { get; init; } = new Dictionary<Guid, TopicStanding>();

    public bool IsNew(Guid topicId) => TodayPlanner.IsNew(topicId, Standings);
}