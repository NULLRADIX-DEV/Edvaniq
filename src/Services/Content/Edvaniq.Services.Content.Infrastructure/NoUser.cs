using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;

namespace Edvaniq.Services.Content.Infrastructure;

// The user where there is none: in the worker and for dotnet ef. A query under the owner filter fails with it, code that
// works for all users says so with IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).
internal sealed class NoUser : ICurrentUser
{
    public string Id => throw new InvalidOperationException(
        $"There is no user here. Ask for all users with IgnoreQueryFilters([{nameof(ServiceDbContext)}.{nameof(ServiceDbContext.OwnerFilter)}]).");
}
