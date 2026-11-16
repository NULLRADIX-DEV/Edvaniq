using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

// Runs in a consumer without a current user, so the owner filter would throw: the queries drop it and name the owner.
internal sealed class TopicCleanup(PlanningDbContext db) : ITopicCleanup
{
    // Loaded and removed through the tracker, not ExecuteDelete: that would run at once, outside the save that stores the
    // new state of the topics.
    public async Task<int> RemoveDoneBlocksAsync(
        string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken)
    {
        var blocks = await db.DoneBlocks.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(block => block.OwnerId == ownerId && block.LearningGoalId == learningGoalId && topicIds.Contains(block.TopicId))
            .ToListAsync(cancellationToken);
        db.DoneBlocks.RemoveRange(blocks);
        return blocks.Count;
    }

    public Task<Session?> ActiveSessionAsync(string ownerId, CancellationToken cancellationToken) =>
        db.Sessions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .FirstOrDefaultAsync(session => session.OwnerId == ownerId && session.EndedAt == null, cancellationToken);

    public async Task<IReadOnlyList<Session>> OnBlockAsync(string ownerId, Guid blockId, CancellationToken cancellationToken) =>
        await db.Sessions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AsNoTracking()
            .Where(session => session.OwnerId == ownerId && session.BlockId == blockId)
            .ToListAsync(cancellationToken);
}