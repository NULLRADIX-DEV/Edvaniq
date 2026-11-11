using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// Today's plan of the user from the token. The blocks done today stay as they were, the open ones are worked out on
// every call (#375).
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
                [.. today.Blocks.Select(entry => new BlockResponse(entry.Block.TopicId, entry.Block.Minutes, KindOf(entry.Block.Kind), entry.Id, entry.Done))],
                (today.Blocks.Count > 0 ? null : today.Plan.Empty) switch
                {
                    TodayPlanEmpty.NoTopics => "noTopics",
                    TodayPlanEmpty.AllMastered => "allMastered",
                    TodayPlanEmpty.QuestionsPending => "questionsPending",
                    _ => null,
                },
                today.MasteryAvailable,
                today.Plan.Shortened,
                today.QuestionPoolsAvailable,
                today.Plan.NotReady);
        });

        return app;
    }

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
// because their question pool isn't ready (#374); "questionsPending" when that is all of them.
internal sealed record TodayPlanResponse(
    DateOnly StudyDay,
    Guid LearningGoalId,
    IReadOnlyList<BlockResponse> Blocks,
    string? EmptyReason,
    bool MasteryAvailable,
    IReadOnlyList<Guid> ShortenedTopicIds,
    bool QuestionPoolsAvailable,
    IReadOnlyList<Guid> NotReadyTopicIds);

// Kind is "learn" or "review". Id stays the same for the same block on every call, Done says whether its session was
// finished (#375).
internal sealed record BlockResponse(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);