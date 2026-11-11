using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// Today's plan of the user from the token. Worked out on every call, nothing is stored (#375).
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
                [.. today.Plan.Blocks.Select(block => new BlockResponse(block.TopicId, block.Minutes, KindOf(block.Kind)))],
                today.Plan.Empty switch
                {
                    TodayPlanEmpty.NoTopics => "noTopics",
                    TodayPlanEmpty.AllMastered => "allMastered",
                    _ => null,
                },
                today.MasteryAvailable,
                today.Plan.Shortened);
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
// because the time is too short (#370), empty when nothing was shortened.
internal sealed record TodayPlanResponse(
    DateOnly StudyDay,
    Guid LearningGoalId,
    IReadOnlyList<BlockResponse> Blocks,
    string? EmptyReason,
    bool MasteryAvailable,
    IReadOnlyList<Guid> ShortenedTopicIds);

// Kind is "learn" or "review".
internal sealed record BlockResponse(Guid TopicId, int Minutes, string Kind);