using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.TestService.Application;
using Edvaniq.Services.TestService.Contracts;
using Edvaniq.Services.TestService.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.TestService.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Bookmarks(TestServiceDbContext db, ICurrentUser currentUser, IEventOutbox outbox) : IBookmarks
{
    public async Task<Bookmark> AddAsync(string url, string title, CancellationToken cancellationToken)
    {
        var bookmark = new Bookmark(currentUser.Id, url, title);
        db.Bookmarks.Add(bookmark);

        // Stored by the same SaveChanges: the event exists exactly when the bookmark does.
        outbox.Add(new BookmarkAdded(bookmark.Id, bookmark.OwnerId));
        await db.SaveChangesAsync(cancellationToken);

        return bookmark;
    }

    public async Task<IReadOnlyList<Bookmark>> ListAsync(CancellationToken cancellationToken) =>
        await db.Bookmarks.AsNoTracking().OrderBy(bookmark => bookmark.Title).ToListAsync(cancellationToken);

    public Task<Bookmark?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Bookmarks.AsNoTracking().FirstOrDefaultAsync(bookmark => bookmark.Id == id, cancellationToken);
}