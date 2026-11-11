using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The check questions of the learning unit "Neues Thema" (docs/spec/glossary.md#checkfrage): two or three questions of
// the topic's pool, the easiest first. They're no kind of their own, each is asked as an attempt and answered like any.
public static class CheckQuestions
{
    public const int Min = 2;
    public const int Max = 3;

    // Easiest first, shuffled within a difficulty, so a repeat doesn't always bring the same ones. Null below Min.
    public static IReadOnlyList<Question>? Pick(IReadOnlyList<Question> pool, Random random)
    {
        if (pool.Count < Min)
        {
            return null;
        }

        var shuffled = pool.ToArray();
        random.Shuffle(shuffled);
        return [.. shuffled.OrderBy(question => question.Difficulty).Take(Max)];
    }
}

public enum CheckOutcome
{
    Ready,
    UnknownTopic,

    // Fewer questions than a check needs, most likely because the pool comes some time after the topic.
    NotReady,
}

public sealed record Checked(CheckOutcome Outcome, IReadOnlyList<(Attempt Attempt, Question Question)> Asked);