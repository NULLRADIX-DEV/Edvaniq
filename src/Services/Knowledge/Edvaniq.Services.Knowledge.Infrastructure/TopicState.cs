using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// Also runs in a consumer without a current user, so the query drops the owner filter and names the owner itself.
internal sealed class TopicState(KnowledgeDbContext db) : ITopicState
{
    public async Task<IReadOnlyList<Guid>> TopicIdsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken)
    {
        var stored = await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId)
            .Select(topic => topic.Id)
            .ToListAsync(cancellationToken);

        var pending = db.ChangeTracker.Entries<Topic>()
            .Where(entry => entry.Entity.OwnerId == ownerId && entry.Entity.LearningGoalId == learningGoalId)
            .ToList();
        var deleted = pending.Where(entry => entry.State == EntityState.Deleted).Select(entry => entry.Entity.Id).ToHashSet();
        var added = pending.Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.Id);

        return [.. stored.Concat(added).Distinct().Where(id => !deleted.Contains(id))];
    }
}