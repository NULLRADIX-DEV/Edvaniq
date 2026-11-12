using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

// Through the owner filter, so a user only ever reads their own blocks.
internal sealed class DoneBlocks(PlanningDbContext db) : IDoneBlocks
{
    public async Task<IReadOnlyList<DoneBlock>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken) =>
        await db.DoneBlocks.AsNoTracking()
            .Where(block => block.LearningGoalId == learningGoalId && block.StudyDay == studyDay)
            .ToListAsync(cancellationToken);

    public void Add(DoneBlock block) => db.DoneBlocks.Add(block);
}