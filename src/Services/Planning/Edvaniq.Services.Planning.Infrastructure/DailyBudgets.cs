using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using MySql.Data.MySqlClient;

namespace Edvaniq.Services.Planning.Infrastructure;

// One row per user, keyed by the owner. Two first saves at once both insert it, the key lets one through and the other
// updates the row on its second try.
internal sealed class DailyBudgets(PlanningDbContext db, ICurrentUser currentUser, IOptions<DailyBudgetOptions> options, TimeProvider time)
    : IDailyBudgets
{
    private const int DuplicateKey = 1062;

    public DailyBudget Default { get; } = DailyBudget.Uniform(options.Value.DefaultMinutes);

    public async Task<DailyBudget> ForUserAsync(string ownerId, CancellationToken cancellationToken)
    {
        // The owner is named, so the filter of the current user must not apply.
        var stored = await db.DailyBudgets.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.OwnerId == ownerId, cancellationToken);
        return stored?.ToBudget() ?? Default;
    }

    public async Task<DailyBudget?> StoredAsync(CancellationToken cancellationToken) =>
        (await db.DailyBudgets.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ToBudget();

    public async Task SetAsync(DailyBudget budget, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var stored = await db.DailyBudgets.FirstOrDefaultAsync(cancellationToken);
            if (stored is null)
            {
                db.DailyBudgets.Add(DailyBudgetSetting.Create(currentUser.Id, budget, now));
            }
            else
            {
                stored.Change(budget, now);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (attempt == 0 && exception.InnerException is MySqlException { Number: DuplicateKey })
            {
                // Another save inserted the row first. The rejected one would stay in the tracker.
                db.ChangeTracker.Clear();
            }
        }
    }
}