using Edvaniq.Services.TestService.Domain;

namespace Edvaniq.Services.TestService.Application;

// The bookmarks of the current user. Infrastructure implements it, the owner filter keeps other users' bookmarks out.
public interface IBookmarks
{
    Task<Bookmark> AddAsync(string url, string title, CancellationToken cancellationToken);

    Task<IReadOnlyList<Bookmark>> ListAsync(CancellationToken cancellationToken);

    Task<Bookmark?> FindAsync(Guid id, CancellationToken cancellationToken);
}