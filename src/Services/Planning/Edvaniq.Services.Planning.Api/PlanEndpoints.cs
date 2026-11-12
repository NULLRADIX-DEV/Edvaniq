using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// Today's plan and the week of the user from the token. The blocks done today stay as they were, the open ones are
// worked out on every call (#375).
internal static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/plan/today", async (TodayPlans plans, CancellationToken cancellationToken) =>
        {
            var today = await plans.TodayAsync(cancellationToken);
            return new TodayPlanResponse(
                today.Plan.StudyDay,
                today.LearningGoalId,
                BlocksOf(today.Blocks),
                ReasonOf(today.Blocks.Count > 0 ? null : today.Plan.Empty),
                today.MasteryAvailable,
                today.Plan.Shortened,
                today.QuestionPoolsAvailable,
                today.Plan.NotReady,
                today.DailyGoal switch
                {
                    DailyGoalState.Reached => "reached",
                    DailyGoalState.Open => "open",
                    _ => "none",
                },
                today.NextBlockId);
        });

        // #379: the days from today on, today exactly as /plan/today.
        app.MapGet("/plan/week", async (WeekPlans plans, CancellationToken cancellationToken) =>
        {
            var week = await plans.ShownAsync(cancellationToken);
            return new WeekPlanResponse(
                week.Today.LearningGoalId,
                [.. week.Days.Select(day => new DayResponse(day.StudyDay, BlocksOf(day.Blocks)))],
                ReasonOf(week.Days.Any(day => day.Blocks.Count > 0) ? null : week.Plan.Empty ?? week.Plan.EmptyDay),
                week.Today.MasteryAvailable,
                week.Plan.Shortened,
                week.Today.QuestionPoolsAvailable,
                week.Plan.NotReady);
        });

        return app;
    }

    private static List<BlockResponse> BlocksOf(IReadOnlyList<DayBlock> blocks) =>
        [.. blocks.Select(entry => new BlockResponse(entry.Block.TopicId, entry.Block.Minutes, KindOf(entry.Block.Kind), entry.Id, entry.Done))];

    private static string? ReasonOf(TodayPlanEmpty? empty) => empty switch
    {
        TodayPlanEmpty.NoTopics => "noTopics",
        TodayPlanEmpty.AllMastered => "allMastered",
        TodayPlanEmpty.QuestionsPending => "questionsPending",
        _ => null,
    };

    private static string KindOf(BlockKind kind) => kind switch
    {
        BlockKind.Review => "review",
        _ => "learn",
    };
}

// EmptyReason is null while there are blocks. MasteryAvailable false: LearningEngine didn't answer, so the blocks don't
// put weaker topics first, and the client can say so. ShortenedTopicIds: topics left without learn time this week
// because the time is too short (#370), empty when nothing was shortened. QuestionPoolsAvailable false: Assessment
// didn't answer, so topics whose questions aren't ready yet may have blocks too. NotReadyTopicIds: topics left out
// because their question pool isn't ready (#374); "questionsPending" when that is all of them. DailyGoal (#381):
// "reached" when every block is done, "open" while one isn't, "none" on a day without blocks.
// NextBlockId (#407): the block "Jetzt lernen" goes on with, a started one first. Null with "reached": all done; null
// with "none": no blocks today.
internal sealed record TodayPlanResponse(
    DateOnly StudyDay,
    Guid LearningGoalId,
    IReadOnlyList<BlockResponse> Blocks,
    string? EmptyReason,
    bool MasteryAvailable,
    IReadOnlyList<Guid> ShortenedTopicIds,
    bool QuestionPoolsAvailable,
    IReadOnlyList<Guid> NotReadyTopicIds,
    string DailyGoal,
    Guid? NextBlockId);

// #379: Days from today on, 7 without an exam, with one only those before the exam day, at least today. A day without
// blocks has an empty list. EmptyReason is null as soon as one day has blocks, the rest as in TodayPlanResponse.
internal sealed record WeekPlanResponse(
    Guid LearningGoalId,
    IReadOnlyList<DayResponse> Days,
    string? EmptyReason,
    bool MasteryAvailable,
    IReadOnlyList<Guid> ShortenedTopicIds,
    bool QuestionPoolsAvailable,
    IReadOnlyList<Guid> NotReadyTopicIds);

internal sealed record DayResponse(DateOnly StudyDay, IReadOnlyList<BlockResponse> Blocks);

// Kind is "learn" or "review". Id stays the same for the same block on every call, Done says whether its session was
// finished (#375).
internal sealed record BlockResponse(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);