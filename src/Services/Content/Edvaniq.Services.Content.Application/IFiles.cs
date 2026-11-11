using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The uploaded files of the current user. Infrastructure implements it, the owner filter keeps other users' files out:
// a foreign file looks exactly like one that doesn't exist. Only the API has it, the worker reads a file by the id it
// finds on the document.
public interface IFiles
{
    // Stores the content under a new id, owned by the current user. Checking type and size is the caller's job.
    Task<StoredFile> SaveAsync(Stream content, CancellationToken cancellationToken);

    // Null if the user has no file with this id.
    Task<Stream?> OpenAsync(Guid id, CancellationToken cancellationToken);

    // False if the user has no file with this id.
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

// The files of a user who is gone. Removes only the bytes, the rows go with IUserDataRemover.
public interface IUserFiles
{
    Task DeleteAllOfAsync(string userId, CancellationToken cancellationToken);
}