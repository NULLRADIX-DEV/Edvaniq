using System.Diagnostics;

using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// Runs in the worker, never in a request: reads the text of every uploaded file page by page (FileText), and
// hands it to its document. One file after the other, so the worker never holds two at once. Text and pages go in one
// save, then the splitter takes the document like a pasted one. A file that can't be read fails its document with a
// reason. A Word file fails with the hint to save it as PDF, until Word files can be read.
public sealed class FileTextExtractor(
    IServiceScopeFactory scopes,
    FileStore files,
    TimeProvider time,
    ILogger<FileTextExtractor> logger) : BackgroundService
{
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
                    if (await ReadWaitingAsync(stoppingToken) == Batch)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    ContentLog.ReadRoundFailed(logger, exception, Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many documents it took on.
    public async Task<int> ReadWaitingAsync(CancellationToken cancellationToken)
    {
        var waiting = await WaitingAsync(cancellationToken);
        foreach (var id in waiting)
        {
            await ReadAsync(id, cancellationToken);
        }

        return waiting.Count;
    }

    // The oldest documents of all users whose file still has to be read.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents
            .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Uploaded && document.FileId != null && document.TextExtractedAt == null)
            .OrderBy(document => document.CreatedAt)
            .Select(document => document.Id)
            .Take(Batch)
            .ToListAsync(cancellationToken);
    }

    public async Task ReadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is not { WaitsForText: true, FileId: { } fileId, FileFormat: { } format })
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        IReadOnlyList<string>? pages = null;
        await using (var content = files.OpenRead(fileId))
        {
            if (content is null)
            {
                ContentLog.FileMissing(logger, documentId);
                document.Fail(Document.FileUnreadable);
            }
            else
            {
                try
                {
                    pages = FileText.PagesOf(content, format, cancellationToken);
                }
                catch (FileRefusedException refused)
                {
                    document.Fail(refused.Reason);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    ContentLog.FileUnreadable(logger, documentId, exception.GetType().Name);
                    document.Fail(Document.FileUnreadable);
                }
            }
        }

        if (pages is not null)
        {
            // A new attempt after a failure reads the file again, the pages of before go in the same save.
            db.DocumentPages.RemoveRange(await db.DocumentPages.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(page => page.DocumentId == documentId)
                .ToListAsync(cancellationToken));
            db.DocumentPages.AddRange(document.TakeText(pages, time.GetUtcNow().UtcDateTime));
            ContentLog.FileRead(logger, documentId, pages.Count, document.Text.Length, watch.ElapsedMilliseconds);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}