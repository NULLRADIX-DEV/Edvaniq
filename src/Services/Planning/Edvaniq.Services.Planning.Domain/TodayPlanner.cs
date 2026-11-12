using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Planning.Domain;

// Today's blocks for the topics of one learning goal, weaker topics first (#210). The first day of the rolling plan
// without an exam (#371), so today gets its share of the week: weight size × (100 - mastery), the heaviest introduced
// topic gets the review block first, the heaviest the first learn block, ties go by the order of topicIds, which is the
// order of the material (#380). Mastery and state are LearningEngine's (#367): a topic is new exactly when
// LearningEngine says so or knows nothing of it. A pure function, the same input always gives the same plan.
public static class TodayPlanner
{
    // The size of a topic sizes doesn't name.
    public const int DefaultSize = 1;

    // LearningEngine starts a topic it knows at 50, even before the first answer. No entry at all means it hasn't heard
    // of the topic yet (TopicsChanged still on its way, or LearningEngine couldn't be asked), so nothing has been learned
    // there: the topic counts as weakest, and as new, so it gets no review before it was ever introduced.
    public const int MasteryOfATopicLearningEngineDoesNotKnow = 0;

    // Today is the study day of now, so 03:30 still plans yesterday. readyTopics: the topics whose question pool
    // Assessment calls ready (#374). Learn unit, quiz and check questions all draw from the pool, so any other topic
    // gets no block and doesn't take part in shortening either. Null when Assessment couldn't be asked: then every
    // topic is planned as before, a block that can't be practised yet beats no plan. Foundations: the topics with
    // subtopics (#909). Sizes: how much there is to learn per topic (#380), DefaultSize for one it doesn't name.
    public static TodayPlan Plan(
        IReadOnlyCollection<Guid> topicIds,
        IReadOnlyDictionary<Guid, TopicStanding> standings,
        DailyBudget budget,
        DateTime utcNow,
        IReadOnlySet<Guid>? readyTopics = null,
        IReadOnlySet<Guid>? foundations = null,
        IReadOnlyDictionary<Guid, int>? sizes = null)
    {
        ArgumentNullException.ThrowIfNull(topicIds);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(budget);

        var today = StudyDay.Of(utcNow);

        if (topicIds.Count == 0)
        {
            return new TodayPlan(today, [], TodayPlanEmpty.NoTopics);
        }

        var distinct = topicIds.Distinct().ToList();
        List<Guid> notReady = readyTopics is null ? [] : [.. distinct.Where(id => !readyTopics.Contains(id)).Order()];
        if (notReady.Count == distinct.Count)
        {
            return new TodayPlan(today, [], TodayPlanEmpty.QuestionsPending) { NotReady = notReady };
        }

        var topics = distinct
            .Select((id, rank) => (Id: id, Rank: rank))
            .Where(topic => !notReady.Contains(topic.Id))
            .Select(topic => ToPlan(topic.Id, standings) with
            {
                IsFoundation = foundations?.Contains(topic.Id) == true,
                Size = sizes?.GetValueOrDefault(topic.Id, DefaultSize) ?? DefaultSize,
                Rank = topic.Rank,
            })
            .ToList();
        var week = StudyPlanner.Plan(topics, budget, today, examDay: null);
        var blocks = week.Days.FirstOrDefault(day => day.StudyDay == today)?.Blocks ?? [];
        return new TodayPlan(today, blocks, blocks.Count == 0 ? TodayPlanEmpty.AllMastered : null)
        {
            Shortened = week.Shortened,
            NotReady = notReady,
        };
    }

    // Mastery kept in 0 to 100, so a wrong value from outside can't break the plan. A state Planning doesn't know is
    // one LearningEngine worked out for a topic it knows, so it isn't new.
    private static TopicToPlan ToPlan(Guid topicId, IReadOnlyDictionary<Guid, TopicStanding> standings) =>
        standings.TryGetValue(topicId, out var standing)
            ? new TopicToPlan(
                topicId,
                DefaultSize,
                Math.Clamp(standing.Mastery, 0, 100),
                IsNew: IsNew(topicId, standings),
                IsWeakness: standing.State == TopicState.Weakness)
            : new TopicToPlan(topicId, DefaultSize, MasteryOfATopicLearningEngineDoesNotKnow, IsNew: true);

    // New exactly when LearningEngine says so or knows nothing of the topic, also when it couldn't be asked (#377).
    public static bool IsNew(Guid topicId, IReadOnlyDictionary<Guid, TopicStanding> standings)
    {
        ArgumentNullException.ThrowIfNull(standings);
        return !standings.TryGetValue(topicId, out var standing) || standing.State == TopicState.New;
    }
}

// The blocks of today in the order they come up. Empty says why there are none, null while there are blocks.
// Shortened: the topics that got no learn time in the week because it is too short (#370), by id. NotReady: the topics
// left out because their question pool isn't ready (#374), by id.
public sealed record TodayPlan(DateOnly StudyDay, IReadOnlyList<Block> Blocks, TodayPlanEmpty? Empty)
{
    public IReadOnlyList<Guid> Shortened { get; init; } = [];

    public IReadOnlyList<Guid> NotReady { get; init; } = [];
}

public enum TodayPlanEmpty
{
    // The learning goal has no topics yet. The client points to adding material.
    NoTopics,

    // Every topic is at 100, nothing left to learn today.
    AllMastered,

    // There are topics, but none has a ready question pool yet, so none can be practised.
    QuestionsPending,
}