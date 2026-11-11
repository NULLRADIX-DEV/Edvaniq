using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Planning.Domain;

// Today's blocks for the topics of one learning goal, weaker topics first (#210). The first day of the rolling plan
// without an exam (#371), so today gets its share of the week: weight 100 - mastery, the heaviest topic gets the review
// block first and the first learn block after it, ties go by topic id. Every topic counts as introduced until Planning
// learns which are new (#364). A pure function, the same input always gives the same plan.
public static class TodayPlanner
{
    // There is no size per topic yet, so every topic counts the same and only the mastery decides.
    public const int Size = 1;

    // LearningEngine starts a topic it knows at 50, even before the first answer. No entry at all means it hasn't heard
    // of the topic yet (TopicsChanged still on its way), so nothing has been learned there: the topic counts as weakest.
    public const int MasteryOfATopicLearningEngineDoesNotKnow = 0;

    // Today is the study day of now, so 03:30 still plans yesterday.
    public static TodayPlan Plan(
        IReadOnlyCollection<Guid> topicIds, IReadOnlyDictionary<Guid, int> masteries, DailyBudget budget, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(topicIds);
        ArgumentNullException.ThrowIfNull(masteries);
        ArgumentNullException.ThrowIfNull(budget);

        var today = StudyDay.Of(utcNow);

        if (topicIds.Count == 0)
        {
            return new TodayPlan(today, [], TodayPlanEmpty.NoTopics);
        }

        var topics = topicIds
            .Distinct()
            .Select(id => new TopicToPlan(id, Size, MasteryOf(id, masteries)))
            .ToList();
        var blocks = StudyPlanner.Plan(topics, budget, today, examDay: null)
            .Days.FirstOrDefault(day => day.StudyDay == today)?.Blocks ?? [];
        return new TodayPlan(today, blocks, blocks.Count == 0 ? TodayPlanEmpty.AllMastered : null);
    }

    // Kept in 0 to 100, so a wrong value from outside can't break the plan.
    private static int MasteryOf(Guid topicId, IReadOnlyDictionary<Guid, int> masteries) =>
        masteries.TryGetValue(topicId, out var mastery)
            ? Math.Clamp(mastery, 0, 100)
            : MasteryOfATopicLearningEngineDoesNotKnow;
}

// The blocks of today in the order they come up. Empty says why there are none, null while there are blocks.
public sealed record TodayPlan(DateOnly StudyDay, IReadOnlyList<Block> Blocks, TodayPlanEmpty? Empty);

public enum TodayPlanEmpty
{
    // The learning goal has no topics yet. The client points to adding material.
    NoTopics,

    // Every topic is at 100, nothing left to learn today.
    AllMastered,
}