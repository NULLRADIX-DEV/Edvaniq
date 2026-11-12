using Edvaniq.BuildingBlocks.Application;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.Web;

// The token of a deleted account is turned away like an invalid one (#953, docs/tokens.md#gültigkeit). Part of the
// default and fallback policy, so it covers every endpoint for a user. A service without IDeletedUsers (the gateway,
// Identity) lets every token through here. The lookup runs in authorization, not in the token handler: that one turns
// every exception into a 401, and a database error must stay a 5xx instead of looking like a logout.
internal sealed class NotDeletedUserRequirement : IAuthorizationRequirement;

internal sealed class NotDeletedUserHandler(IServiceProvider services) : AuthorizationHandler<NotDeletedUserRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, NotDeletedUserRequirement requirement)
    {
        var deleted = services.GetService<IDeletedUsers>();
        if (deleted is null
            || context.User.FindFirst(CurrentUser.IdClaim)?.Value is not { } userId
            || !deleted.IsDeleted(userId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

// A failed NotDeletedUserRequirement answers 401 like an invalid token, not 403. Everything else as usual.
internal sealed class DeletedUserResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult) =>
        authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<NotDeletedUserRequirement>().Any() == true
            ? context.ChallengeAsync()
            : fallback.HandleAsync(next, context, policy, authorizeResult);
}