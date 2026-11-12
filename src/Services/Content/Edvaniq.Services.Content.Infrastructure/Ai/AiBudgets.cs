using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// The sum of this UTC month: every attempt of existing users plus the daily sums of deleted ones, as in the cost report.
// Both tables have their time indexed, the sum stays one quick query.
internal sealed class AiBudgets(ContentDbContext db, TimeProvider time) : IAiBudget
{
    public async Task<AiBudgetState> StateAsync(CancellationToken cancellationToken)
    {
        var month = AiSwitch.MonthOf(time.GetUtcNow().UtcDateTime);
        var budget = await db.AiSwitches.AsNoTracking()
            .Where(item => item.Id == AiSwitch.TheOne)
            .Select(item => item.MonthlyBudget)
            .FirstOrDefaultAsync(cancellationToken);
        var attempts = await db.AiAttempts.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(attempt => attempt.CalledAt >= month)
            .SumAsync(attempt => attempt.Cost ?? 0, cancellationToken);
        var archived = await db.AiUsageDays
            .Where(sum => sum.Day >= month)
            .SumAsync(sum => sum.Cost, cancellationToken);
        return new AiBudgetState(budget, attempts + archived);
    }

    public async Task<bool> SetAsync(decimal? budget, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var stored = await TheOneAsync(now, cancellationToken);
        var changed = stored.SetBudget(budget, now);
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    public async Task<(bool Warn, bool Exhausted)> NoteAsync(AiBudgetState state, CancellationToken cancellationToken)
    {
        if (!state.Warned)
        {
            return (false, false);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var stored = await TheOneAsync(now, cancellationToken);
        var warn = stored.MarkWarned(now);
        var exhausted = state.Exhausted && stored.MarkExhausted(now);
        if (warn || exhausted)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return (warn, exhausted);
    }

    private async Task<AiSwitch> TheOneAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (await db.AiSwitches.SingleOrDefaultAsync(item => item.Id == AiSwitch.TheOne, cancellationToken) is { } stored)
        {
            return stored;
        }

        stored = AiSwitch.Running(now);
        db.AiSwitches.Add(stored);
        return stored;
    }
}