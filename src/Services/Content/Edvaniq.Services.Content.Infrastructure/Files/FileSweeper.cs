using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// Runs in the API, at its start and then once a day. Removes what no row points at: bytes left by a crash between
// writing and saving, files after a restore of an older database, and .part files of uploads that never finished. Only
// what's older than an hour, so an upload in progress keeps its bytes until its row is saved.
public sealed class FileSweeper(
    IServiceScopeFactory scopes,
    FileStore store,
    TimeProvider time,
    ILogger<FileSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan MinAge = TimeSpan.FromHours(1);

    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                    ContentLog.SweepFailed(logger, exception, Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many files it removed.
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var before = time.GetUtcNow().UtcDateTime - MinAge;
        var old = store.List().Where(bytes => bytes.WrittenAt < before).ToList();
        if (old.Count == 0)
        {
            return 0;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var removed = 0;
        foreach (var bytes in old)
        {
            if (bytes.Partial)
            {
                store.DeletePart(bytes.Id);
            }
            else if (!await db.StoredFiles.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AnyAsync(file => file.Id == bytes.Id, cancellationToken))
            {
                store.Delete(bytes.Id);
            }
            else
            {
                continue;
            }

            ContentLog.FileSwept(logger, bytes.Id);
            removed++;
        }

        return removed;
    }
}