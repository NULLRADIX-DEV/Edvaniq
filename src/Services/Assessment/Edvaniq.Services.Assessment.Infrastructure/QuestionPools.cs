using Edvaniq.Services.Assessment.Application;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Assessment.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class QuestionPools(AssessmentDbContext db) : IQuestionPools
{
    public async Task<TopicPool?> FindAsync(Guid topicId, CancellationToken cancellationToken)
    {
        if (!await db.KnownTopics.AnyAsync(topic => topic.TopicId == topicId, cancellationToken))
        {
            return null;
        }

        var questions = await db.Questions.CountAsync(question => question.TopicId == topicId, cancellationToken);
        return new TopicPool(topicId, questions);
    }

    // Two queries however many topics: the topics, then the count of questions per topic at once.
    public async Task<IReadOnlyList<TopicPool>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken)
    {
        var topics = db.KnownTopics.AsNoTracking();
        if (learningGoalId is { } goal)
        {
            topics = topics.Where(topic => topic.LearningGoalId == goal);
        }

        var topicIds = await topics.Select(topic => topic.TopicId).ToListAsync(cancellationToken);
        if (topicIds.Count == 0)
        {
            return [];
        }

        var counts = await db.Questions
            .Where(question => question.TopicId != null && topicIds.Contains(question.TopicId.Value))
            .GroupBy(question => question.TopicId!.Value)
            .Select(group => new { TopicId = group.Key, Questions = group.Count() })
            .ToDictionaryAsync(group => group.TopicId, group => group.Questions, cancellationToken);
        return [.. topicIds.Order().Select(id => new TopicPool(id, counts.GetValueOrDefault(id)))];
    }
}