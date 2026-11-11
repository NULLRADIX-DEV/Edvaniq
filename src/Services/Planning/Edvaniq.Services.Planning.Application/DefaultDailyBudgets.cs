using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Users can't set a budget of their own until M5, so every user has the configured default on every day. Nothing is
// stored per user: a user without an own value simply gets the default.
public sealed class DefaultDailyBudgets(DailyBudgetOptions options) : IDailyBudgets
{
    private readonly DailyBudget budget = DailyBudget.Uniform(options.DefaultMinutes);

    public Task<DailyBudget> ForUserAsync(string ownerId, CancellationToken cancellationToken) => Task.FromResult(budget);
}