using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The week as the list shows it (#379): the rolling week of TodayPlans, cut before the exam day of the goal in the plan.
public sealed class WeekPlans(TodayPlans plans, ILearningGoals goals)
{
    public async Task<WeekPlanResult> ShownAsync(CancellationToken cancellationToken)
    {
        var week = await plans.WeekAsync(cancellationToken);
        var goal = await goals.FindWithDefaultAsync(week.Today.LearningGoalId, cancellationToken);
        var shown = TodayPlanner.DaysShown(week.Plan.StudyDay, goal?.ExamDate);
        return week with { Days = [.. week.Days.Take(shown)] };
    }
}