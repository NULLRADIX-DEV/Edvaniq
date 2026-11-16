using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Assessment.Infrastructure;

// Runs in a consumer without a current user, so the owner filter would throw: the queries drop it and name the owner.
internal sealed class KnownTopics(AssessmentDbContext db) : IKnownTopics
{
    public Task<TopicsSnapshot?> SnapshotAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
        db.TopicsSnapshots.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .FirstOrDefaultAsync(snapshot => snapshot.OwnerId == ownerId && snapshot.LearningGoalId == learningGoalId, cancellationToken);

    public Task<bool> IsKnownAsync(string ownerId, Guid topicId, CancellationToken cancellationToken) =>
        db.KnownTopics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .AnyAsync(topic => topic.OwnerId == ownerId && topic.TopicId == topicId, cancellationToken);

    public async Task<IReadOnlyList<KnownTopic>> ListAsync(
        string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
        await db.KnownTopics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && (topic.LearningGoalId == learningGoalId || topicIds.Contains(topic.TopicId)))
            .ToListAsync(cancellationToken);

    public void Add(TopicsSnapshot snapshot) => db.TopicsSnapshots.Add(snapshot);

    public void Add(KnownTopic topic) => db.KnownTopics.Add(topic);

    // An open attempt could not be answered without its question, an answered one holds options of a question that is
    // gone. The learning records stay with LearningEngine.
    public async Task RemoveAsync(KnownTopic topic, CancellationToken cancellationToken)
    {
        db.KnownTopics.Remove(topic);
        var questions = await db.Questions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(question => question.OwnerId == topic.OwnerId && question.TopicId == topic.TopicId)
            .ToListAsync(cancellationToken);
        var ids = questions.Select(question => question.Id).ToList();
        db.Attempts.RemoveRange(await db.Attempts.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(attempt => attempt.OwnerId == topic.OwnerId && ids.Contains(attempt.QuestionId))
            .ToListAsync(cancellationToken));
        db.Questions.RemoveRange(questions);

        // Its block quizzes have nothing left to ask (#412).
        db.BlockQuizzes.RemoveRange(await db.BlockQuizzes.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(quiz => quiz.OwnerId == topic.OwnerId && quiz.TopicId == topic.TopicId)
            .ToListAsync(cancellationToken));
    }
}