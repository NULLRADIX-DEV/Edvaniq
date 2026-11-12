using Edvaniq.Services.Planning.Application;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

// Through the owner filter, so a user only ever reads their own sessions.
internal sealed class StartedBlocks(PlanningDbContext db) : IStartedBlocks
{
    public async Task<IReadOnlySet<Guid>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken)
    {
        var ids = await db.Sessions.AsNoTracking()
            .Where(session => session.LearningGoalId == learningGoalId && session.StudyDay == studyDay)
            .Select(session => session.BlockId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}