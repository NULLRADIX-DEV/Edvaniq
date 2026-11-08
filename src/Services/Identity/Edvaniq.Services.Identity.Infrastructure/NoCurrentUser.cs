using Edvaniq.BuildingBlocks.Application;

namespace Edvaniq.Services.Identity.Infrastructure;

// Identity keeps no data that belongs to a user yet, so nothing asks who the user is. When something does, it fails
// instead of seeing everyone. Replaced by the token check once Identity has endpoints for a logged-in user.
internal sealed class NoCurrentUser : ICurrentUser
{
    public string Id => throw new InvalidOperationException("Identity has no user data, so there is no current user.");
}