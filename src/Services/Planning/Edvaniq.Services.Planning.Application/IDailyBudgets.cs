using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The daily budget of a user: the one they stored, otherwise the configured default.
public interface IDailyBudgets
{
    // The configured default (DailyBudget:DefaultMinutes) on every day.
    DailyBudget Default { get; }

    // For the planner. Names the owner, the planner may run without a current user.
    Task<DailyBudget> ForUserAsync(string ownerId, CancellationToken cancellationToken);

    // The budget the current user stored, null if they never did.
    Task<DailyBudget?> StoredAsync(CancellationToken cancellationToken);

    // Stores the budget of the current user, replacing the one before.
    Task SetAsync(DailyBudget budget, CancellationToken cancellationToken);
}