using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.LearningEngine.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Masteries(LearningEngineDbContext db, TimeProvider time) : IMasteries
{
    public async Task<int?> FindAsync(Guid topicId, CancellationToken cancellationToken)
    {
        if (!await db.KnownTopics.AnyAsync(topic => topic.TopicId == topicId, cancellationToken))
        {
            return null;
        }

        var records = await db.LearningRecords.AsNoTracking()
            .Where(record => record.TopicId == topicId)
            .ToListAsync(cancellationToken);
        return Mastery.Percent(Mastery.Of(records, time.GetUtcNow().UtcDateTime));
    }

    // Two queries however many topics: the topics, then all their records at once (index on owner and topic).
    public async Task<IReadOnlyList<TopicMastery>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken)
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

        var records = (await db.LearningRecords.AsNoTracking()
                .Where(record => topicIds.Contains(record.TopicId))
                .ToListAsync(cancellationToken))
            .ToLookup(record => record.TopicId);
        var now = time.GetUtcNow().UtcDateTime;
        return [.. topicIds.Order().Select(id => new TopicMastery(id, Mastery.Percent(Mastery.Of(records[id], now))))];
    }
}