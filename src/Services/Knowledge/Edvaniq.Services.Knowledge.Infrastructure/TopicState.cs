using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// Also runs in a consumer without a current user, so the query drops the owner filter and names the owner itself.
internal sealed class TopicState(KnowledgeDbContext db, TimeProvider time) : ITopicState
{
    // Once per save and goal: a set the tracker already has as changed counts.
    public async Task TouchAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken)
    {
        if (db.ChangeTracker.Entries<TopicSet>().Any(entry => entry.Entity.OwnerId == ownerId
                && entry.Entity.LearningGoalId == learningGoalId
                && entry.State is EntityState.Added or EntityState.Modified))
        {
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var set = await db.TopicSets.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .FirstOrDefaultAsync(item => item.OwnerId == ownerId && item.LearningGoalId == learningGoalId, cancellationToken);
        if (set is null)
        {
            db.TopicSets.Add(TopicSet.Of(ownerId, learningGoalId, now));
        }
        else
        {
            set.Touch(now);
        }
    }

    public async Task<IReadOnlyList<TopicInState>> TopicsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken)
    {
        var stored = await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId)
            .Select(topic => new TopicInState(topic.Id, topic.ParentId, topic.Hidden, topic.CreatedAt, topic.Position, topic.Name, 0))
            .ToListAsync(cancellationToken);

        // The tracker knows what the save will change: topics added, deleted, moved to another goal or hung under another
        // parent. Its state wins over the stored one.
        var pending = db.ChangeTracker.Entries<Topic>().Where(entry => entry.Entity.OwnerId == ownerId).ToList();
        var leaving = pending
            .Where(entry => entry.State == EntityState.Deleted || entry.Entity.LearningGoalId != learningGoalId)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();
        var joining = pending
            .Where(entry => entry.State != EntityState.Deleted && entry.Entity.LearningGoalId == learningGoalId)
            .Select(entry => new TopicInState(
                entry.Entity.Id, entry.Entity.ParentId, entry.Entity.Hidden, entry.Entity.CreatedAt, entry.Entity.Position, entry.Entity.Name, 0));

        var topics = joining.Concat(stored).DistinctBy(topic => topic.Id).Where(topic => !leaving.Contains(topic.Id)).ToList();
        var sections = await SectionsAsync(ownerId, [.. topics.Select(topic => topic.Id)], cancellationToken);
        return [.. topics.Select(topic => topic with { Sections = sections.GetValueOrDefault(topic.Id) })];
    }

    // Sections per topic once the save is done: the deletion of a document (#202) or a new analysis removes and adds them
    // in the same unit of work.
    private async Task<Dictionary<Guid, int>> SectionsAsync(string ownerId, List<Guid> topicIds, CancellationToken cancellationToken)
    {
        var counts = await db.TopicSections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.OwnerId == ownerId && topicIds.Contains(section.TopicId))
            .GroupBy(section => section.TopicId)
            .Select(group => new { TopicId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.TopicId, entry => entry.Count, cancellationToken);

        foreach (var entry in db.ChangeTracker.Entries<TopicSection>().Where(entry => entry.Entity.OwnerId == ownerId))
        {
            var change = entry.State switch
            {
                EntityState.Added => 1,
                EntityState.Deleted => -1,
                _ => 0,
            };
            counts[entry.Entity.TopicId] = counts.GetValueOrDefault(entry.Entity.TopicId) + change;
        }

        return counts;
    }
}