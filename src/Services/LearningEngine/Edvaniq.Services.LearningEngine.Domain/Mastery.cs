namespace Edvaniq.Services.LearningEngine.Domain;

// How well a user masters a topic, 0 to 100 % (docs/spec/glossary.md#mastery). Never stored: it's worked out from the
// learning records of the topic every time, so a redelivered answer can't move it twice and it can always be rebuilt.
// The rule: start at 50, every answer pulls the value part of the way to 100 (correct) or 0 (wrong), harder questions
// pull further. Without answers the value decays towards 0, between two answers and from the last one until now. So
// newer answers weigh more twice over: later answers push out a share of everything before, and older ones have decayed.
// A topic that was practised and then left alone can drop below the start value of a topic never practised. That's
// on purpose: without review the value keeps sinking (A41), and the planner should bring it back.
// Bump RuleVersion whenever a number or step here or a threshold in TopicStates changes, so a value can tell which
// rule made it.
public static class Mastery
{
    // 3 since #364: the answer carries the topic state (thresholds of TopicStates), and since #363 an introduction
    // decides which answers count.
    public const int RuleVersion = 3;

    public const double Start = 50;

    // Share of the way to 100 or 0 one answer moves the value, by difficulty 1 to 3 (Question.MinDifficulty to
    // MaxDifficulty in Assessment). 0.25 for the middle one was the thin rule, a step of 0.1 either side keeps one
    // easy answer noticeable and lets a hard one count clearly more without a single answer swinging the topic.
    public const double EasyStep = 0.15;

    public const double MediumStep = 0.25;

    public const double HardStep = 0.35;

    // Without answers the value halves every 90 days: 90 % drops below "sicher" (80) after about two weeks and below
    // 50 after about two and a half months, so a weekend off changes little but weeks without review show.
    public static readonly TimeSpan HalfLife = TimeSpan.FromDays(90);

    // Start is where a topic stands before its first answer. A start of the user's own (placement, M5) replaces the
    // default and leaves every step of the rule as it is.
    // From: where earlier records that are gone left the fold (MasteryCarry, #365). It goes on from there exactly as if
    // those records were still there, and replaces the start.
    public static double Of(IEnumerable<LearningRecord> records, DateTime now, double start = Start, MasteryFold? from = null) =>
        Fold(records, start, from) is { } fold
            ? Math.Clamp(Decay(fold.Value, now - fold.At), 0, 100)
            : Math.Clamp(start, 0, 100);

    // The value right after the last record, before any decay until now. Null without records and without from.
    public static MasteryFold? Fold(IEnumerable<LearningRecord> records, double start = Start, MasteryFold? from = null)
    {
        var fold = from;
        foreach (var record in records.OrderBy(record => record.AnsweredAt).ThenBy(record => record.AnswerId))
        {
            var value = fold is { } previous ? Decay(previous.Value, record.AnsweredAt - previous.At) : Math.Clamp(start, 0, 100);
            value += StepFor(record.Difficulty) * ((record.Correct ? 100 : 0) - value);
            fold = new MasteryFold(value, record.AnsweredAt, record.Correct, fold?.LastCorrect);
        }

        return fold;
    }

    // Cards (M4) bring their own, smaller step. A question without a difficulty counts as a middle one.
    public static double StepFor(int? difficulty) =>
        Math.Clamp(difficulty ?? 2, 1, 3) switch
        {
            1 => EasyStep,
            2 => MediumStep,
            _ => HardStep,
        };

    // An answer stamped after now (clocks differ a bit) doesn't raise the value.
    public static double Decay(double value, TimeSpan elapsed) =>
        elapsed <= TimeSpan.Zero ? value : value * Math.Pow(0.5, elapsed / HalfLife);

    // Whole percent, like the thresholds of the topic states (49, 50, 79, 80).
    public static int Percent(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}