using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;

using MySql.Data.MySqlClient;

namespace Edvaniq.Services.Planning.Infrastructure;

// The default goal has the same key for every request of a user (LearningGoal.DefaultIdFor). Two first requests at once
// both insert it, the database lets one through, and the other finds the goal on its second look.
internal sealed class LearningGoals(PlanningDbContext db, ICurrentUser currentUser, TimeProvider time) : ILearningGoals
{
    private const int DuplicateKey = 1062;

    public async Task<LearningGoal> DefaultAsync(CancellationToken cancellationToken)
    {
        var id = LearningGoal.DefaultIdFor(currentUser.Id);
        var stored = await FindAsync(id, cancellationToken);
        if (stored is not null)
        {
            return stored;
        }

        db.LearningGoals.Add(LearningGoal.CreateDefault(currentUser.Id, time.GetUtcNow().UtcDateTime));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: DuplicateKey })
        {
            // The rejected goal would stay in the tracker and come back instead of the stored one.
            db.ChangeTracker.Clear();
        }

        return await FindAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The default learning goal was not stored.");
    }

    private Task<LearningGoal?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.LearningGoals.AsNoTracking().FirstOrDefaultAsync(goal => goal.Id == id, cancellationToken);
}