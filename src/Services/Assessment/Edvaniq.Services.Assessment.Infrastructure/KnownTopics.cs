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

    public async Task<IReadOnlyList<KnownTopic>> ListAsync(
        string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
        await db.KnownTopics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && (topic.LearningGoalId == learningGoalId || topicIds.Contains(topic.TopicId)))
            .ToListAsync(cancellationToken);

    public void Add(TopicsSnapshot snapshot) => db.TopicsSnapshots.Add(snapshot);

    public void Add(KnownTopic topic) => db.KnownTopics.Add(topic);

    public void Remove(KnownTopic topic) => db.KnownTopics.Remove(topic);
}