using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The one way the quiz picks questions of a topic, for a single question (POST /attempts) and for a block's quiz
// (POST /quizzes). Leaving out the ones seen last and putting weaknesses first is #387.
public static class QuestionSelection
{
    // Up to count questions of the pool, at random, none twice.
    public static IReadOnlyList<Question> Pick(IReadOnlyList<Question> pool, int count, Random random)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(random);
        var shuffled = pool.ToArray();
        random.Shuffle(shuffled);
        return [.. shuffled.Take(count)];
    }
}