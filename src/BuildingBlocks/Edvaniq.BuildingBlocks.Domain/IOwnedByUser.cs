namespace Edvaniq.BuildingBlocks.Domain;

// Data that belongs to exactly one user. Queries only ever return the rows of the current user (ServiceDbContext).
public interface IOwnedByUser
{
    string OwnerId { get; }
}
