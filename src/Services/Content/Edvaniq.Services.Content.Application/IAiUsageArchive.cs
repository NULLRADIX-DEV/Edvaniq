namespace Edvaniq.Services.Content.Application;

// For the UserDeleted handler: adds the AI attempts of a user to the daily sums that belong to nobody, before the
// user's rows go.
public interface IAiUsageArchive
{
    // Only marks the changes, the caller's SaveChanges stores them.
    void Archive(string userId);
}