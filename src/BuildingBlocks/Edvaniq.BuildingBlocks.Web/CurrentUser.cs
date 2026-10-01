using Edvaniq.BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;

namespace Edvaniq.BuildingBlocks.Web;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // The subject of the token. The authorization policy lets no request through without it.
    public const string IdClaim = "sub";

    public string Id => accessor.HttpContext?.User.FindFirst(IdClaim)?.Value
        ?? throw new InvalidOperationException("The request has no authenticated user.");
}
