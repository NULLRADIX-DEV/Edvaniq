using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;

using MySql.Data.MySqlClient;

namespace Edvaniq.Services.Planning.Infrastructure;

// Through the owner filter, so a user only ever reads their own blocks.
internal sealed class DoneBlocks(PlanningDbContext db) : IDoneBlocks
{
    private const int DuplicateKey = 1062;

    public async Task<IReadOnlyList<DoneBlock>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken) =>
        await db.DoneBlocks.AsNoTracking()
            .Where(block => block.LearningGoalId == learningGoalId && block.StudyDay == studyDay)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DoneBlock block, CancellationToken cancellationToken)
    {
        if (await db.DoneBlocks.AnyAsync(stored => stored.Id == block.Id, cancellationToken))
        {
            return;
        }

        db.DoneBlocks.Add(block);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: DuplicateKey })
        {
            // Done at the same time somewhere else, the first one stays.
            db.Entry(block).State = EntityState.Detached;
        }
    }
}