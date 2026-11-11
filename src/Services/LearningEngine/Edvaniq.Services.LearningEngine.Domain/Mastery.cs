namespace Edvaniq.Services.LearningEngine.Domain;

// How well a user masters a topic, 0 to 100 % (docs/spec/glossary.md#mastery). Never stored: it's worked out from the
// learning records of the topic every time, so a redelivered answer can't move it twice and it can always be rebuilt.
// The thin rule for now: every answer pulls the value a fixed share of the way to 100 (correct) or 0 (wrong), so newer
// answers already weigh more. Difficulty, decay and the placement start value come with M3.
public static class Mastery
{
    public const double Start = 50;

    public const double Step = 0.25;

    public static double Of(IEnumerable<LearningRecord> records) =>
        records
            .OrderBy(record => record.AnsweredAt)
            .ThenBy(record => record.AnswerId)
            .Aggregate(Start, (value, record) => value + (Step * ((record.Correct ? 100 : 0) - value)));

    // Whole percent, like the thresholds of the topic states (49, 50, 79, 80).
    public static int Percent(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}