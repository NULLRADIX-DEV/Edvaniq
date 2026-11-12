namespace Edvaniq.BuildingBlocks.Application;

// Everything a service does when a user is deleted, files included. Its UserDeleted handler runs it, and the sweep runs
// it again for rows a handler stored while the deletion ran (docs/service-internals.md#nutzer-löschen). Like the
// handler it only marks the rows, the caller's SaveChanges removes them.
public interface IUserDeletion
{
    Task DeleteAsync(string userId, CancellationToken cancellationToken);
}