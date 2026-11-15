using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Assessment.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Attempts(AssessmentDbContext db) : IAttempts
{
    public Task<bool> KnowsTopicAsync(Guid topicId, CancellationToken cancellationToken) =>
        db.KnownTopics.AnyAsync(topic => topic.TopicId == topicId, cancellationToken);

    public async Task<IReadOnlyList<Question>> QuestionsOfAsync(Guid topicId, CancellationToken cancellationToken) =>
        await db.Questions.AsNoTracking()
            .Where(question => question.TopicId == topicId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, DateTime>> LastAnsweredAsync(IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken)
    {
        var answered = await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.Answered && attempt.AnsweredAt != null && topicIds.Contains(attempt.TopicId))
            .GroupBy(attempt => attempt.QuestionId)
            .Select(group => new { QuestionId = group.Key, Last = group.Max(attempt => attempt.AnsweredAt!.Value) })
            .ToListAsync(cancellationToken);
        return answered.ToDictionary(entry => entry.QuestionId, entry => entry.Last);
    }

    public Task<Question?> FindQuestionAsync(Guid id, CancellationToken cancellationToken) =>
        db.Questions.AsNoTracking().FirstOrDefaultAsync(question => question.Id == id, cancellationToken);

    public Task<Attempt?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Attempts.FirstOrDefaultAsync(attempt => attempt.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Attempt>> AnsweredInAsync(
        Guid? blockId, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var quizId = blockId is { } block
            ? await db.BlockQuizzes.AsNoTracking()
                .Where(quiz => quiz.BlockId == block)
                .Select(quiz => (Guid?)quiz.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        if (quizId is null && sessionIds.Count == 0)
        {
            return [];
        }

        return await db.Attempts.AsNoTracking()
            .Where(attempt => attempt.Answered
                && ((quizId != null && attempt.QuizId == quizId)
                    || (attempt.SessionId != null && sessionIds.Contains(attempt.SessionId.Value))))
            .OrderBy(attempt => attempt.AnsweredAt)
            .ThenBy(attempt => attempt.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(Attempt attempt) => db.Attempts.Add(attempt);

    // Answered is a concurrency token, so the later of two answers updates no row. Its transaction rolls back with
    // the outbox row, and the tracker forgets it so the stored answer can be read.
    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}