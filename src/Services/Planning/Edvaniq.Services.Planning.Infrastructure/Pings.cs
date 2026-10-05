using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;
using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

// Every ping is a row of its own: inserts never collide, even when two pings arrive at the same time.
internal sealed class Pings(PlanningDbContext db, TimeProvider time) : IPings
{
    public async Task<PingRecord> RecordAsync(CancellationToken cancellationToken)
    {
        var ping = new PingRecord(time.GetUtcNow().UtcDateTime);
        db.Pings.Add(ping);
        await db.SaveChangesAsync(cancellationToken);

        // Without AsNoTracking EF would hand back the object from its memory instead of what the database stored.
        return await db.Pings.AsNoTracking().SingleAsync(stored => stored.Id == ping.Id, cancellationToken);
    }
}
