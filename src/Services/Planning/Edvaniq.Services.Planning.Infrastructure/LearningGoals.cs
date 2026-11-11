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
        var stored = await FindAnyAsync(id, cancellationToken);
        if (stored is not null)
        {
            return stored;
        }

        // Losing the insert is fine, the goal of the call that won is the same one.
        await TryInsertAsync(LearningGoal.CreateDefault(currentUser.Id, time.GetUtcNow().UtcDateTime), cancellationToken);
        return await FindAnyAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("The default learning goal was not stored.");
    }

    public async Task<LearningGoal> CreateAsync(LearningGoalFields fields, CancellationToken cancellationToken)
    {
        var defaultId = LearningGoal.DefaultIdFor(currentUser.Id);
        var now = time.GetUtcNow().UtcDateTime;

        // Twice at most: losing the insert of the default goal to DefaultAsync leaves a default goal to take over.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // One statement, so of calls at the same time only one finds the name still empty.
            var taken = await db.LearningGoals
                .Where(goal => goal.Id == defaultId && goal.Name == null)
                .ExecuteUpdateAsync(
                    goal => goal
                        .SetProperty(stored => stored.CreatedAt, now)
                        .SetProperty(stored => stored.Kind, fields.Kind)
                        .SetProperty(stored => stored.Name, fields.Name)
                        .SetProperty(stored => stored.ExamType, fields.ExamType)
                        .SetProperty(stored => stored.ExamDate, fields.ExamDate)
                        .SetProperty(stored => stored.Level, fields.Level)
                        .SetProperty(stored => stored.OutputLanguage, fields.OutputLanguage)
                        .SetProperty(stored => stored.Goal, fields.Goal),
                    cancellationToken);
            if (taken == 1)
            {
                return await StoredAsync(defaultId, cancellationToken);
            }

            if (await db.LearningGoals.AnyAsync(goal => goal.Id == defaultId, cancellationToken))
            {
                break;
            }

            if (await TryInsertAsync(LearningGoal.Create(defaultId, currentUser.Id, fields, now), cancellationToken))
            {
                return await StoredAsync(defaultId, cancellationToken);
            }
        }

        var goal = LearningGoal.Create(Guid.CreateVersion7(time.GetUtcNow()), currentUser.Id, fields, now);
        db.LearningGoals.Add(goal);
        await db.SaveChangesAsync(cancellationToken);
        return await StoredAsync(goal.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<LearningGoal>> ListAsync(CancellationToken cancellationToken) =>
        await db.LearningGoals.AsNoTracking()
            .Where(goal => goal.Name != null)
            .OrderBy(goal => goal.CreatedAt)
            .ThenBy(goal => goal.Id)
            .ToListAsync(cancellationToken);

    public Task<LearningGoal?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.LearningGoals.AsNoTracking().FirstOrDefaultAsync(goal => goal.Id == id && goal.Name != null, cancellationToken);

    private async Task<bool> TryInsertAsync(LearningGoal goal, CancellationToken cancellationToken)
    {
        db.LearningGoals.Add(goal);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: DuplicateKey })
        {
            // The rejected goal would stay in the tracker and come back instead of the stored one.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<LearningGoal> StoredAsync(Guid id, CancellationToken cancellationToken) =>
        await FindAnyAsync(id, cancellationToken) ?? throw new InvalidOperationException("The learning goal was not stored.");

    private Task<LearningGoal?> FindAnyAsync(Guid id, CancellationToken cancellationToken) =>
        db.LearningGoals.AsNoTracking().FirstOrDefaultAsync(goal => goal.Id == id, cancellationToken);
}