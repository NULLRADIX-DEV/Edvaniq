using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// The daily budget of the user from the token. Weekdays by their English names: unlike numbers they can't be mixed up
// between ISO (1 = Monday) and .NET (0 = Sunday).
internal static class DailyBudgetEndpoints
{
    public static IEndpointRouteBuilder MapDailyBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var budget = app.MapGroup("/daily-budget");

        budget.MapGet("/", async (IDailyBudgets budgets, CancellationToken cancellationToken) =>
            await budgets.StoredAsync(cancellationToken) is { } stored
                ? DailyBudgetResponse.From(stored, isDefault: false)
                : DailyBudgetResponse.From(budgets.Default, isDefault: true));

        // All seven days every time, a missing one is an error rather than a day nobody meant to change.
        budget.MapPut("/", async (DailyBudgetRequest request, IDailyBudgets budgets, CancellationToken cancellationToken) =>
        {
            var parsed = DailyBudget.Parse(request.ToDictionary(), out var errors);
            if (parsed is null)
            {
                return Results.ValidationProblem(errors);
            }

            await budgets.SetAsync(parsed, cancellationToken);
            return Results.Ok(DailyBudgetResponse.From(parsed, isDefault: false));
        });

        return app;
    }
}

internal sealed record DailyBudgetRequest(
    int? Monday,
    int? Tuesday,
    int? Wednesday,
    int? Thursday,
    int? Friday,
    int? Saturday,
    int? Sunday)
{
    public Dictionary<DayOfWeek, int?> ToDictionary() =>
        new()
        {
            [DayOfWeek.Monday] = Monday,
            [DayOfWeek.Tuesday] = Tuesday,
            [DayOfWeek.Wednesday] = Wednesday,
            [DayOfWeek.Thursday] = Thursday,
            [DayOfWeek.Friday] = Friday,
            [DayOfWeek.Saturday] = Saturday,
            [DayOfWeek.Sunday] = Sunday,
        };
}

// IsDefault: the user never stored a budget, the minutes are the configured default.
internal sealed record DailyBudgetResponse(
    int Monday,
    int Tuesday,
    int Wednesday,
    int Thursday,
    int Friday,
    int Saturday,
    int Sunday,
    bool IsDefault)
{
    public static DailyBudgetResponse From(DailyBudget budget, bool isDefault) =>
        new(
            budget.MinutesOn(DayOfWeek.Monday),
            budget.MinutesOn(DayOfWeek.Tuesday),
            budget.MinutesOn(DayOfWeek.Wednesday),
            budget.MinutesOn(DayOfWeek.Thursday),
            budget.MinutesOn(DayOfWeek.Friday),
            budget.MinutesOn(DayOfWeek.Saturday),
            budget.MinutesOn(DayOfWeek.Sunday),
            isDefault);
}