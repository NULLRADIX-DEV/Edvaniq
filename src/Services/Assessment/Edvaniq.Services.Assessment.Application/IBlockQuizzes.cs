using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The block quizzes of the current user. The owner filter keeps everyone else's out.
public interface IBlockQuizzes
{
    Task<BlockQuiz?> FindAsync(Guid blockId, CancellationToken cancellationToken);

    // The quiz's attempts with their questions, always in the same order.
    Task<IReadOnlyList<(Attempt Attempt, Question Question)>> QuestionsOfAsync(Guid quizId, CancellationToken cancellationToken);

    // Quiz and attempts in one save. False when the user has a quiz for the block already, the database lets only one
    // through, and nothing of this one is stored.
    Task<bool> TryAddAsync(BlockQuiz quiz, IReadOnlyList<Attempt> attempts, CancellationToken cancellationToken);
}