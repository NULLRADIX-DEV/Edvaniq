using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Api;

// The user's own daily quotas: GET /quota for the client, and the early answer when a new job would not fit. The worker
// decides with the same counts (IPageQuota, IGenerationQuota) once pages and topics are known.
internal static class QuotaEndpoints
{
    public static IEndpointRouteBuilder MapQuotaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/quota", async (IPageQuota pages, IGenerationQuota generations, ICurrentUser user, CancellationToken cancellationToken) =>
        {
            var pageState = await pages.StateOfAsync(user.Id, cancellationToken);
            var generationState = await generations.StateOfAsync(user.Id, cancellationToken);
            return new QuotaResponse(
                pageState.Left, pageState.PagesPerDay, generationState.Left, generationState.GenerationsPerDay, pageState.ResetsAt);
        });

        return app;
    }

    // 429 if pages are more than the user has left today, or with no pages known yet (an upload, a retry) if none are
    // left, and when no generation is left either. A retry has its pages charged already and asks for generations only.
    public static async Task<IResult?> RefusalAsync(
        IPageQuota pageQuota,
        IGenerationQuota generationQuota,
        ICurrentUser user,
        int? pages,
        TimeProvider time,
        CancellationToken cancellationToken,
        bool askPages = true)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var pageState = await pageQuota.StateOfAsync(user.Id, cancellationToken);
        var reason = !askPages ? null
            : pages is { } needed ? !pageState.Allows(needed) ? PageQuota.TooManyPages(needed, pageState.Left, now) : null
            : pageState.Left == 0 ? PageQuota.UsedUp(now) : null;
        if (reason is null && (await generationQuota.StateOfAsync(user.Id, cancellationToken)).Left == 0)
        {
            reason = GenerationQuota.UsedUp(now);
        }

        return reason is null
            ? null
            : Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: reason,
                extensions: new Dictionary<string, object?> { ["resetsAt"] = pageState.ResetsAt });
    }
}

// Read by the client (#307). ResetsAt in UTC, the same for both quotas.
internal sealed record QuotaResponse(int PagesLeftToday, int PagesPerDay, int? GenerationsLeftToday, int? GenerationsPerDay, DateTime ResetsAt);