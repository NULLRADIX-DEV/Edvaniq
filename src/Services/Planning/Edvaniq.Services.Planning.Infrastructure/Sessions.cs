using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;

using MySql.Data.MySqlClient;

namespace Edvaniq.Services.Planning.Infrastructure;

// Through the owner filter, so a user only ever sees their own sessions.
internal sealed class Sessions(PlanningDbContext db) : ISessions
{
    private const int DuplicateKey = 1062;

    public Task<Session?> ActiveAsync(CancellationToken cancellationToken) =>
        db.Sessions.AsNoTracking().FirstOrDefaultAsync(session => session.EndedAt == null, cancellationToken);

    public Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sessions.AsNoTracking().FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task<Session?> FindForChangeAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sessions.FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task<Session?> LastAbortedAsync(Guid blockId, CancellationToken cancellationToken) =>
        db.Sessions.AsNoTracking()
            .Where(session => session.BlockId == blockId && session.Outcome == SessionOutcome.Aborted)
            .OrderByDescending(session => session.EndedAt)
            .ThenByDescending(session => session.Id)
            .FirstOrDefaultAsync(cancellationToken);

    // LastActivityAt is a concurrency token: the later of two changes updates no row, and the tracker forgets the
    // session so the next read gets the stored one.
    public async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken)
    {
        db.Sessions.Add(session);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: DuplicateKey })
        {
            // The unique index on ActiveOwnerId: another start of the user was first.
            db.Entry(session).State = EntityState.Detached;
            return false;
        }
    }
}