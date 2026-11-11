namespace Edvaniq.Services.LearningEngine.Domain;

// How well a user masters a topic, 0 to 100 % (docs/spec/glossary.md#mastery). Never stored: it's worked out from the
// learning records of the topic every time, so a redelivered answer can't move it twice and it can always be rebuilt.
// The rule: start at 50, every answer pulls the value part of the way to 100 (correct) or 0 (wrong), harder questions
// pull further. Without answers the value decays towards 0, between two answers and from the last one until now. So
// newer answers weigh more twice over: later answers push out a share of everything before, and older ones have decayed.
// A topic that was practised and then left alone can drop below the start value of a topic never practised. That's
// on purpose: without review the value keeps sinking (A41), and the planner should bring it back.
// Bump RuleVersion whenever a number or step here changes, so a value can tell which rule made it.
public static class Mastery
{
    public const int RuleVersion = 2;

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

    public static double Of(IEnumerable<LearningRecord> records, DateTime now)
    {
        var value = Start;
        DateTime? last = null;
        foreach (var record in records.OrderBy(record => record.AnsweredAt).ThenBy(record => record.AnswerId))
        {
            if (last is { } previous)
            {
                value = Decay(value, record.AnsweredAt - previous);
            }

            value += StepFor(record.Difficulty) * ((record.Correct ? 100 : 0) - value);
            last = record.AnsweredAt;
        }

        if (last is { } latest)
        {
            value = Decay(value, now - latest);
        }

        return Math.Clamp(value, 0, 100);
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