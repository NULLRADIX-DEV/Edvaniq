using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Api;

// The user's own daily quota: GET /quota for the client, and the early answer when a new job would not fit. The worker
// decides with the same count (IPageQuota) once a file's pages are known.
internal static class QuotaEndpoints
{
    public static IEndpointRouteBuilder MapQuotaEndpoints(this IEndpointRouteBuilder app)
    {
        // Generations come with #302, until then they are null.
        app.MapGet("/quota", async (IPageQuota quota, ICurrentUser user, CancellationToken cancellationToken) =>
        {
            var state = await quota.StateOfAsync(user.Id, cancellationToken);
            return new QuotaResponse(state.Left, state.PagesPerDay, null, null, state.ResetsAt);
        });

        return app;
    }

    // 429 if pages more than the user has left today, or with no pages known yet (an upload) if nothing is left.
    public static async Task<IResult?> RefusalAsync(IPageQuota quota, ICurrentUser user, int? pages, TimeProvider time, CancellationToken cancellationToken)
    {
        var state = await quota.StateOfAsync(user.Id, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        string? reason = pages is { } needed
            ? needed > state.Left ? PageQuota.TooManyPages(needed, state.Left, now) : null
            : state.Left == 0 ? PageQuota.UsedUp(now) : null;
        return reason is null
            ? null
            : Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: reason,
                extensions: new Dictionary<string, object?> { ["resetsAt"] = state.ResetsAt });
    }
}

// Read by the client (#307). ResetsAt in UTC.
internal sealed record QuotaResponse(int PagesLeftToday, int PagesPerDay, int? GenerationsLeftToday, int? GenerationsPerDay, DateTime ResetsAt);