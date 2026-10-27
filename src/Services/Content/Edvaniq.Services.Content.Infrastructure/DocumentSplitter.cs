using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// Runs in the worker and splits every uploaded document into sections. The API only stores the text and answers at once,
// the next round here picks the document up from the database. Sections and the new status are saved together, so a
// worker that stops halfway leaves the document uploaded and the next round starts it again.
public sealed class DocumentSplitter(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<DocumentSplitter> logger) : BackgroundService
{
    // Documents per round. A full round means more are waiting, the next one starts without a pause.
    private const int Batch = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    if (await SplitWaitingAsync(stoppingToken) == Batch)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    ContentLog.SplitRoundFailed(logger, exception, Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many documents it took on.
    public async Task<int> SplitWaitingAsync(CancellationToken cancellationToken)
    {
        var waiting = await WaitingAsync(cancellationToken);
        foreach (var id in waiting)
        {
            await SplitAsync(id, cancellationToken);
        }

        return waiting.Count;
    }

    // The oldest uploaded documents of all users.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents
            .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Uploaded)
            .OrderBy(document => document.CreatedAt)
            .Select(document => document.Id)
            .Take(Batch)
            .ToListAsync(cancellationToken);
    }

    public async Task SplitAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document?.Status != DocumentStatus.Uploaded)
        {
            return;
        }

        // A document that comes back to Uploaded, say for a new attempt, still has its sections from before. They go
        // in the same save as the new ones, so no place in the text has two sections.
        db.Sections.RemoveRange(await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.DocumentId == documentId)
            .ToListAsync(cancellationToken));

        try
        {
            db.Sections.AddRange(document.Split());
        }
        catch (Exception exception)
        {
            // A bug in splitting fails this one document instead of stopping every round at it.
            ContentLog.SplitFailed(logger, exception, documentId);
            document.Fail(Document.SplitFailed);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
