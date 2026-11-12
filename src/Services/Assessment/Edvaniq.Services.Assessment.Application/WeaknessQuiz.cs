using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The weaknesses of a learning goal as LearningEngine names them, the severest first. Assessment never works a state
// out itself (#367). Null when LearningEngine can't be asked.
public interface IWeaknesses
{
    Task<IReadOnlyList<Guid>?> OfLearningGoalAsync(Guid learningGoalId, CancellationToken cancellationToken);
}

// A quiz of the user's weaknesses in a learning goal (#387, A38): all questions come from the weaknesses, the topics
// take turns, the severest first, each by the rules of QuestionSelection. Asked again it brings a new set, like
// POST /attempts.
public sealed class WeaknessQuiz(IWeaknesses weaknesses, IAttempts attempts, TimeProvider time)
{
    public const int MaxSize = BlockQuizOpener.MaxSize;

    public async Task<WeaknessQuizResult> AskAsync(Guid learningGoalId, int size, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSize);

        if (await weaknesses.OfLearningGoalAsync(learningGoalId, cancellationToken) is not { } topicIds)
        {
            return new(WeaknessQuizOutcome.Unavailable);
        }

        if (topicIds.Count == 0)
        {
            return new(WeaknessQuizOutcome.NoWeaknesses);
        }

        var pools = new List<(Guid TopicId, IReadOnlyList<Question> Questions)>();
        foreach (var topicId in topicIds.Distinct())
        {
            if (await attempts.KnowsTopicAsync(topicId, cancellationToken)
                && await attempts.QuestionsOfAsync(topicId, cancellationToken) is { Count: > 0 } questions)
            {
                pools.Add((topicId, questions));
            }
        }

        if (pools.Count == 0)
        {
            return new(WeaknessQuizOutcome.NoQuestions);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var lastAnswered = await attempts.LastAnsweredAsync([.. pools.Select(pool => pool.TopicId)], cancellationToken);
        var selection = QuestionSelection.PickAcross([.. pools.Select(pool => pool.Questions)], lastAnswered, size, now, Random.Shared);
        var topicOf = pools.SelectMany(pool => pool.Questions.Select(question => (question.Id, pool.TopicId)))
            .DistinctBy(entry => entry.Id)
            .ToDictionary(entry => entry.Id, entry => entry.TopicId);
        var asked = selection.Questions.Select(question => (Attempt.Ask(question, topicOf[question.Id], now), question)).ToList();
        foreach (var (attempt, _) in asked)
        {
            attempts.Add(attempt);
        }

        await attempts.SaveAsync(cancellationToken);
        return new(WeaknessQuizOutcome.Ready, asked, selection.PoolTooSmall);
    }
}

public enum WeaknessQuizOutcome
{
    Ready,

    // LearningEngine didn't answer.
    Unavailable,
    NoWeaknesses,

    // The weaknesses have no questions yet.
    NoQuestions,
}

public sealed record WeaknessQuizResult(
    WeaknessQuizOutcome Outcome, IReadOnlyList<(Attempt Attempt, Question Question)>? Questions = null, bool PoolTooSmall = false);