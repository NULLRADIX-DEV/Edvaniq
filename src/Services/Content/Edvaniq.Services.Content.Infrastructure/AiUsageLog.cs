using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Domain;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// Stores every attempt of an AI call right after it, in a save of its own: the money is spent even when the save of
// the document fails later, say on a second worker. A database that is away for a moment costs the rows of this call,
// not the analysis.
public sealed class AiUsageLog(IServiceScopeFactory scopes, ILogger<AiUsageLog> logger)
{
    public async Task RecordAsync(
        Document document, string purpose, string promptVersion, IReadOnlyList<AiTry> tries, string fallbackModel,
        CancellationToken cancellationToken)
    {
        if (tries.Count == 0)
        {
            return;
        }

        var callId = Guid.NewGuid();
        var attempts = tries.Select(attempt => AiAttempt.Of(
            callId, attempt.Number, document, purpose, attempt.ModelId ?? fallbackModel, promptVersion, attempt.StartedAt,
            attempt.Duration, attempt.InputTokens, attempt.OutputTokens, attempt.Cost, OutcomeOf(attempt.Outcome))).ToList();
        ContentLog.AiUsage(
            logger, purpose, document.Id, attempts.Count, attempts.Sum(attempt => attempt.InputTokens),
            attempts.Sum(attempt => attempt.OutputTokens), attempts.Sum(attempt => attempt.Cost ?? 0),
            attempts.Count(attempt => attempt.Cost is null), attempts.Sum(attempt => attempt.DurationMs), attempts[^1].Model);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
            db.AiAttempts.AddRange(attempts);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ContentLog.AiUsageNotStored(logger, exception, purpose, document.Id);
        }
    }

    private static AiAttemptOutcome OutcomeOf(AiTryOutcome outcome) => outcome switch
    {
        AiTryOutcome.Answered => AiAttemptOutcome.Answered,
        AiTryOutcome.Unusable => AiAttemptOutcome.Unusable,
        _ => AiAttemptOutcome.Failed,
    };
}