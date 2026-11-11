using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Today's plan of the current user for the default learning goal. Topics come from this service's own database, the
// mastery from one query to LearningEngine, so a plan costs at most one call to another service. Without LearningEngine
// the plan still comes, only without weaker topics first (docs/spec/acceptance.md A92).
public sealed class TodayPlans(
    IKnownTopics knownTopics, IMasterySource masteries, IDailyBudgets budgets, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<TodayPlanResult> TodayAsync(CancellationToken cancellationToken)
    {
        var owner = currentUser.Id;
        var goal = LearningGoal.DefaultIdFor(owner);

        var topics = await knownTopics.ActiveAsync(owner, goal, cancellationToken);
        var mastery = topics.Count == 0 ? null : await masteries.OfLearningGoalAsync(goal, cancellationToken);
        var budget = await budgets.ForUserAsync(owner, cancellationToken);

        var plan = TodayPlanner.Plan(topics, mastery ?? new Dictionary<Guid, int>(), budget, time.GetUtcNow().UtcDateTime);
        return new TodayPlanResult(goal, plan, MasteryAvailable: topics.Count == 0 || mastery is not null);
    }
}

public sealed record TodayPlanResult(Guid LearningGoalId, TodayPlan Plan, bool MasteryAvailable);