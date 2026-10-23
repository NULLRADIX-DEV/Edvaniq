namespace Edvaniq.BuildingBlocks.Application;

// Removes all data of one user from the service's database, for the handler of a deleted user. It acts for no user of a
// request, so it sees the rows of everyone.
public interface IUserDataRemover
{
    // Marks every row of the user for removal and returns how many. Like a handler, it stores nothing: the SaveChanges
    // of the caller removes them, together with the inbox entry.
    int RemoveAll(string userId);
}