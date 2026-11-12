using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

internal sealed class AiSwitches(ContentDbContext db, TimeProvider time) : IAiSwitch
{
    public async Task<bool> IsPausedAsync(CancellationToken cancellationToken) =>
        await db.AiSwitches.AsNoTracking().AnyAsync(item => item.Id == AiSwitch.TheOne && item.Paused, cancellationToken);

    public async Task<bool> SetAsync(bool paused, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var stored = await db.AiSwitches.SingleOrDefaultAsync(item => item.Id == AiSwitch.TheOne, cancellationToken);
        if (stored is null)
        {
            stored = AiSwitch.Running(now);
            db.AiSwitches.Add(stored);
        }

        var changed = stored.Set(paused, now);
        await db.SaveChangesAsync(cancellationToken);

        // Documents postponed after an outage don't wait out their delay: due now, they meet the pause in the next round
        // and fail like the others.
        if (paused)
        {
            await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(document => document.NextAttemptAt != null)
                .ExecuteUpdateAsync(update => update.SetProperty(document => document.NextAttemptAt, (DateTime?)null), cancellationToken);
        }

        return changed;
    }
}

// Every AI call of Content goes through this, also the operator's ai-check. With the AI paused it never reaches the
// provider and fails with AiPausedException, with this month's budget used up with AiBudgetExhaustedException, like an
// outage would but on purpose. The first call of a month that finds 80 % or all of the budget used tells the operator,
// once each, in a log line with a fixed event (AiBudgetWarning, AiBudgetExhausted) and only the percentage.
public sealed class AiGate(IChatClient inner, IServiceScopeFactory scopes, ILogger<AiGate> logger) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await ThrowIfPausedAsync(cancellationToken);
        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ThrowIfPausedAsync(cancellationToken);
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task ThrowIfPausedAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        if (await scope.ServiceProvider.GetRequiredService<IAiSwitch>().IsPausedAsync(cancellationToken))
        {
            throw new AiPausedException();
        }

        var budget = scope.ServiceProvider.GetRequiredService<IAiBudget>();
        var state = await budget.StateAsync(cancellationToken);
        var (warn, exhausted) = await budget.NoteAsync(state, cancellationToken);
        if (warn)
        {
            ContentLog.AiBudgetWarning(logger, state.Percent ?? 0);
        }

        if (exhausted)
        {
            ContentLog.AiBudgetExhausted(logger, state.Percent ?? 0);
        }

        if (state.Exhausted)
        {
            throw new AiBudgetExhaustedException();
        }
    }
}

public sealed class AiPausedException() : Exception("The operator paused the AI.");

public sealed class AiBudgetExhaustedException() : Exception("The AI budget of this month is used up.");