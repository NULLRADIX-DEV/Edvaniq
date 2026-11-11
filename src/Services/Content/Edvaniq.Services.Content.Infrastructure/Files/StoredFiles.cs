using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// Bytes in the file store, the owner in the database. A file is written before its row and deleted before its row, so a
// row never points at bytes of somebody else. What a crash leaves behind, bytes without a row, FileSweeper removes.
internal sealed class StoredFiles(ContentDbContext db, FileStore store, ICurrentUser currentUser, TimeProvider time) : IFiles, IUserFiles
{
    public async Task<StoredFile> SaveAsync(Stream content, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var length = await store.WriteAsync(id, content, cancellationToken);
        var file = StoredFile.Of(id, currentUser.Id, length, time.GetUtcNow().UtcDateTime);
        try
        {
            db.StoredFiles.Add(file);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            db.Entry(file).State = EntityState.Detached;
            store.Delete(id);
            throw;
        }

        return file;
    }

    // The owner filter is the check: no row of this user, no bytes.
    public async Task<Stream?> OpenAsync(Guid id, CancellationToken cancellationToken) =>
        await db.StoredFiles.AnyAsync(file => file.Id == id, cancellationToken) ? store.OpenRead(id) : null;

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (file is null)
        {
            return false;
        }

        store.Delete(id);
        db.StoredFiles.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // All filters off, like the remover that takes the rows afterwards.
    public async Task DeleteAllOfAsync(string userId, CancellationToken cancellationToken)
    {
        var ids = await db.StoredFiles.IgnoreQueryFilters()
            .Where(file => file.OwnerId == userId)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            store.Delete(id);
        }
    }
}