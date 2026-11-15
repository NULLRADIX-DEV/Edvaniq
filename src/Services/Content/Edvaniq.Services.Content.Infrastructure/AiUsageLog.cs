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
        Document document, string purpose, string promptVersion, AiReference reference, IReadOnlyList<AiTry> tries,
        string fallbackModel, CancellationToken cancellationToken)
    {
        // The demo asks no provider and costs nothing, so the cost log has nothing to say about it (#411, A93).
        if (tries.Count == 0 || document.Origin == DocumentOrigin.Demo)
        {
            return;
        }

        var callId = Guid.NewGuid();
        var attempts = tries.Select(attempt => AiAttempt.Of(
            callId, attempt.Number, document, purpose, attempt.ModelId ?? fallbackModel, promptVersion, attempt.StartedAt,
            attempt.Duration, attempt.InputTokens, attempt.OutputTokens, attempt.Cost, OutcomeOf(attempt.Outcome), reference)).ToList();
        ContentLog.AiUsage(
            logger, purpose, document.Id, reference.TopicId, reference.SummaryId, callId, attempts.Count,
            attempts.Sum(attempt => attempt.InputTokens), attempts.Sum(attempt => attempt.OutputTokens),
            attempts.Sum(attempt => attempt.Cost ?? 0), attempts.Count(attempt => attempt.Cost is null),
            attempts.Sum(attempt => attempt.DurationMs), promptVersion, attempts[^1].Model);
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

    // A call answered from a stored answer (AiAnswers): no attempt, so no row, only the line in the log.
    public void Reused(Document document, string purpose, string promptVersion, AiReference reference, string model) =>
        ContentLog.AiUsageReused(logger, purpose, document.Id, reference.TopicId, reference.SummaryId, promptVersion, model);

    private static AiAttemptOutcome OutcomeOf(AiTryOutcome outcome) => outcome switch
    {
        AiTryOutcome.Answered => AiAttemptOutcome.Answered,
        AiTryOutcome.Unusable => AiAttemptOutcome.Unusable,
        _ => AiAttemptOutcome.Failed,
    };
}