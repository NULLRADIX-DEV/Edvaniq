namespace Edvaniq.Services.Planning.Domain;

// The extra review once today's plan is done (docs/spec/acceptance.md A62, #408). Not a block of the plan: it never
// counts for the daily goal. A pure function.
public static class BonusReview
{
    public const int Minutes = 10;

    // The topic the bonus review asks about: the weakness with the lowest mastery, else the introduced topic with the
    // lowest mastery, ties by id. Only the given topics with a ready question pool count. The states come from
    // LearningEngine alone (TopicStateRule), there is no threshold here. Null when no topic fits.
    public static Guid? TopicOf(IEnumerable<Guid> topics, IReadOnlyDictionary<Guid, TopicStanding> standings, IEnumerable<Guid> notReady)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(standings);
        ArgumentNullException.ThrowIfNull(notReady);

        var skipped = notReady.ToHashSet();
        var candidates = topics
            .Distinct()
            .Where(topic => !skipped.Contains(topic) && !TodayPlanner.IsNew(topic, standings))
            .Select(topic => (Id: topic, Standing: standings[topic]))
            .ToList();

        return Weakest(candidates.Where(topic => topic.Standing.State == TopicState.Weakness)) ?? Weakest(candidates);
    }

    private static Guid? Weakest(IEnumerable<(Guid Id, TopicStanding Standing)> topics) =>
        topics
            .OrderBy(topic => topic.Standing.Mastery)
            .ThenBy(topic => topic.Id)
            .Select(topic => (Guid?)topic.Id)
            .FirstOrDefault();
}