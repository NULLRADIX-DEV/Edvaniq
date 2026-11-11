using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The daily budget of a user, for the planner. Names the owner, the planner may run without a current user.
public interface IDailyBudgets
{
    Task<DailyBudget> ForUserAsync(string ownerId, CancellationToken cancellationToken);
}