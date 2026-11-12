using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure;

// Fails a document whose run takes longer than AnalysisOptions.Timeout, counted from the paste, upload or retry, so no
// document stays "in analysis" for good, e.g. when its worker keeps dying on it. The user can start it again. One that is
// postponed after an outage of the AI waits on purpose and only counts once it's due again. The status is a concurrency
// token: a worker that finishes the document at the same moment wins or loses as a whole, nothing goes out twice.
public sealed class StuckAnalysisSweep(
    IServiceScopeFactory scopes,
    IOptions<AnalysisOptions> options,
    TimeProvider time,
    ILogger<StuckAnalysisSweep> logger) : BackgroundService
{
    private const int Batch = 50;

    public TimeSpan Timeout { get; set; } = options.Value.Timeout;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SweepInterval;
        try
        {
            while (true)
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    ContentLog.SweepRoundFailed(logger, exception, interval);
                }

                await Task.Delay(interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many documents it marked as failed.
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var startedBefore = now - Timeout;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var stuck = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Uploaded || document.Status == DocumentStatus.Analyzing)
            .Where(document => (document.RetriedAt ?? document.CreatedAt) < startedBefore)
            .Where(document => document.NextAttemptAt == null || document.NextAttemptAt <= now)
            .OrderBy(document => document.RetriedAt ?? document.CreatedAt)
            .Take(Batch)
            .ToListAsync(cancellationToken);

        var failed = 0;
        foreach (var document in stuck)
        {
            var status = document.Status;
            document.Fail(Document.AnalysisTimedOut);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                ContentLog.StuckDocumentFailed(logger, document.Id, status, Timeout);
                failed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A worker got there first, or the user deleted it: either way it's no longer stuck.
                db.Entry(document).State = EntityState.Detached;
            }
        }

        if (failed > 0)
        {
            ContentLog.StuckDocumentsSwept(logger, failed, Timeout);
        }

        return failed;
    }
}