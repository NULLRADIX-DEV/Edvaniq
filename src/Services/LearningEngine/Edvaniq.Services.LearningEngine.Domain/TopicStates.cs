namespace Edvaniq.Services.LearningEngine.Domain;

// The one place that turns mastery and recent answers into a topic state. No other service knows these numbers, they
// ask LearningEngine (#367). Constants, not configuration: the state has to come out the same from the same answers
// everywhere, and a changed threshold bumps Mastery.RuleVersion.
public static class TopicStates
{
    // Whole percent, like Mastery.Percent shows it (A37: 49 is a weakness, 50 and 79 building, 80 secure).
    public const int WeakBelow = 50;

    public const int SecureFrom = 80;

    // "High error rate lately": at least 2 of the last 3 answers wrong, and only once there are 3. Three because the
    // introduction asks two to three check questions (modes.md), and a short window is what lets a topic with mastery
    // still above 50 turn weak after a run of wrong answers (A39). One slip alone never counts.
    public const int RecentAnswers = 3;

    public const int RecentWrongForWeakness = 2;

    // Mastery and state of one topic from all its learning records. Both come from the same counted records and the
    // same whole percent, so the value shown and the state can't disagree. Before the introduction no answer counts:
    // the topic is new and stands at its start value.
    public static TopicStanding Assess(
        IEnumerable<LearningRecord> records, TopicIntroduction? introduction, DateTime now, double start = Mastery.Start)
    {
        var (introduced, counted) = Counted(records.ToList(), introduction);
        var mastery = Mastery.Percent(Mastery.Of(counted, now, start));
        return new TopicStanding(mastery, Of(introduced, mastery, counted));
    }

    // Secure at the last counted answer, not secure now (docs/spec/glossary.md#wiederholung). Since that answer only the
    // decay has moved the value, so this is "below SecureFrom now, above it without the decay". The same Assess at two
    // points in time, so the rule exists once. Null for any other topic.
    public static DecayedTopic? Decayed(
        IEnumerable<LearningRecord> records, TopicIntroduction? introduction, DateTime now, double start = Mastery.Start)
    {
        var all = records.ToList();
        var (introduced, counted) = Counted(all, introduction);
        if (!introduced || counted.Count == 0)
        {
            return null;
        }

        var lastAnsweredAt = counted.Max(record => record.AnsweredAt);
        var then = Assess(all, introduction, lastAnsweredAt, start);
        var current = Assess(all, introduction, now, start);
        return then.State == TopicState.Secure && current.State != TopicState.Secure
            ? new DecayedTopic(current.Mastery, then.Mastery, lastAnsweredAt)
            : null;
    }

    // Before the introduction no answer counts.
    private static (bool Introduced, List<LearningRecord> Counted) Counted(
        List<LearningRecord> records, TopicIntroduction? introduction) =>
        introduction is null
            ? IntroducedByFirstRecord(records)
            : (true, records.Where(introduction.Counts).ToList());

    // Transition until Planning sends SessionCompleted (#395): until then no topic gets an introduction, so one without
    // counts as introduced from its first learning record on, with all its records. Practising keeps moving the mastery.
    // Once a topic has an introduction, only that decides.
    public static (bool Introduced, List<LearningRecord> Counted) IntroducedByFirstRecord(List<LearningRecord> records) =>
        (records.Count > 0, records);

    public static TopicState Of(bool introduced, int mastery, IEnumerable<LearningRecord> records)
    {
        if (!introduced)
        {
            return TopicState.New;
        }

        if (mastery < WeakBelow || HasHighRecentErrorRate(records))
        {
            return TopicState.Weakness;
        }

        return mastery >= SecureFrom ? TopicState.Secure : TopicState.Building;
    }

    // Same order as Mastery.Of, so "the last answers" are the same ones the value saw last.
    public static bool HasHighRecentErrorRate(IEnumerable<LearningRecord> records)
    {
        var recent = records
            .OrderByDescending(record => record.AnsweredAt)
            .ThenByDescending(record => record.AnswerId)
            .Take(RecentAnswers)
            .ToList();
        return recent.Count == RecentAnswers && recent.Count(record => !record.Correct) >= RecentWrongForWeakness;
    }
}