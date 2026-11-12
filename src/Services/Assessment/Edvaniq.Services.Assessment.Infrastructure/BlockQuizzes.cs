using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

using Microsoft.EntityFrameworkCore;

using MySql.Data.MySqlClient;

namespace Edvaniq.Services.Assessment.Infrastructure;

// Through the owner filter, so a block id only ever finds the caller's own quiz.
internal sealed class BlockQuizzes(AssessmentDbContext db) : IBlockQuizzes
{
    private const int DuplicateKey = 1062;

    public Task<BlockQuiz?> FindAsync(Guid blockId, CancellationToken cancellationToken) =>
        db.BlockQuizzes.AsNoTracking().FirstOrDefaultAsync(quiz => quiz.BlockId == blockId, cancellationToken);

    public async Task<IReadOnlyList<(Attempt Attempt, Question Question)>> QuestionsOfAsync(Guid quizId, CancellationToken cancellationToken)
    {
        var rows = await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.QuizId == quizId)
            .Join(db.Questions.AsNoTracking(), attempt => attempt.QuestionId, question => question.Id, (attempt, question) => new { attempt, question })
            .OrderBy(row => row.attempt.Id)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => (row.attempt, row.question))];
    }

    public async Task<bool> TryAddAsync(BlockQuiz quiz, IReadOnlyList<Attempt> attempts, CancellationToken cancellationToken)
    {
        db.BlockQuizzes.Add(quiz);
        db.Attempts.AddRange(attempts);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: DuplicateKey })
        {
            // The unique index on owner and block: another call for the block was first.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}