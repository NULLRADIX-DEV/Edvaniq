using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

internal sealed class GoalProfiles(PlanningDbContext db) : IGoalProfiles
{
    // Past the owner filter on purpose: the caller is Content's worker, not a user (IGoalProfiles).
    public Task<LearningGoal?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.LearningGoals.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AsNoTracking()
            .FirstOrDefaultAsync(goal => goal.Id == id, cancellationToken);
}