namespace Edvaniq.BuildingBlocks.Application;

// For the UserDeleted handler. Runs outside a request, so it sees every user's rows.
public interface IUserDataRemover
{
    // Only marks the rows and returns how many, the caller's SaveChanges removes them.
    int RemoveAll(string userId);
}