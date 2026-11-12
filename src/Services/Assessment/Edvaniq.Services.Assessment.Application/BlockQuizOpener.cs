using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The quiz of a block (#393): made on the first call, the same on every later one, with the answered questions marked.
// A session broken off and started again on the block thus only has the open ones left. Answers go through the attempts
// as always, so the learning records don't change.
public sealed class BlockQuizOpener(IBlockQuizzes quizzes, IAttempts attempts, ICurrentUser currentUser, TimeProvider time)
{
    public const int MaxSize = 20;

    public async Task<OpenedQuiz> OpenAsync(Guid topicId, Guid blockId, int size, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSize);

        if (!await attempts.KnowsTopicAsync(topicId, cancellationToken))
        {
            return new(QuizOutcome.UnknownTopic);
        }

        if (await quizzes.FindAsync(blockId, cancellationToken) is { } existing)
        {
            return await OfAsync(existing, topicId, cancellationToken);
        }

        var pool = await attempts.QuestionsOfAsync(topicId, cancellationToken);
        if (pool.Count == 0)
        {
            return new(QuizOutcome.NoQuestions);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var quiz = BlockQuiz.Create(currentUser.Id, blockId, topicId, now);
        var asked = QuestionSelection.Pick(pool, size, Random.Shared).Select(question => Attempt.AskIn(quiz, question, now)).ToList();
        if (await quizzes.TryAddAsync(quiz, asked, cancellationToken))
        {
            return await OfAsync(quiz, topicId, cancellationToken);
        }

        // Another call for the same block was first, its quiz counts.
        return await quizzes.FindAsync(blockId, cancellationToken) is { } winner
            ? await OfAsync(winner, topicId, cancellationToken)
            : throw new InvalidOperationException("A quiz lost against one for the same block that is gone.");
    }

    // A block belongs to one topic, so another topic for the same block is a mistake of the caller.
    private async Task<OpenedQuiz> OfAsync(BlockQuiz quiz, Guid topicId, CancellationToken cancellationToken) =>
        quiz.TopicId != topicId
            ? new(QuizOutcome.OtherTopic)
            : new(QuizOutcome.Ready, quiz, await quizzes.QuestionsOfAsync(quiz.Id, cancellationToken));
}

public enum QuizOutcome
{
    Ready,
    UnknownTopic,
    NoQuestions,

    // The user has a quiz for this block, for another topic.
    OtherTopic,
}

public sealed record OpenedQuiz(QuizOutcome Outcome, BlockQuiz? Quiz = null, IReadOnlyList<(Attempt Attempt, Question Question)>? Questions = null);