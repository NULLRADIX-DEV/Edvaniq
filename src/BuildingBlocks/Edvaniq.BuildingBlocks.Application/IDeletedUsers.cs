namespace Edvaniq.BuildingBlocks.Application;

// Users this service has deleted (UserDeleted). The token check asks it, so a token issued before the deletion stops
// working here too (docs/tokens.md#gültigkeit).
public interface IDeletedUsers
{
    bool IsDeleted(string userId);
}