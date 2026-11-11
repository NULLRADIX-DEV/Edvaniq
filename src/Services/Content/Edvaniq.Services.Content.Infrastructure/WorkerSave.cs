using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// The one save of a worker step on a document. The user may delete the document while the step runs, then the save
// fails on the missing row (or on its sections' key) and takes everything with it, the events too. That's no error,
// the worker goes on with the next document. Any other failure goes up as before.
public static class WorkerSave
{
    // True if saved, false if the document is gone.
    public static async Task<bool> SaveAsync(
        ContentDbContext db, Guid documentId, string step, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AnyAsync(document => document.Id == documentId, cancellationToken))
            {
                throw;
            }

            ContentLog.DocumentDeletedMeanwhile(logger, step, documentId);
            return false;
        }
    }
}