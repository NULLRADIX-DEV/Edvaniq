using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Api;

// A new job for the AI, with this month's budget used up: 429 with the reason the client shows, before anything is stored.
// The same sum the worker's gate uses (IAiBudget), no amount in the answer.
internal static class BudgetRefusal
{
    public static async Task<IResult?> OfAsync(IAiBudget budget, CancellationToken cancellationToken) =>
        (await budget.StateAsync(cancellationToken)).Exhausted
            ? Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: Document.AiBudgetReached)
            : null;
}