using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Messaging;

// Deletes sent outbox rows and inbox rows once they are older than the retention. Unsent events stay. Every process may
// run it, deleting an old row twice does no harm.
internal sealed class MessagingCleanup(
    IServiceScopeFactory scopes,
    MessagingStore store,
    IMessageTransport transport,
    IOptions<MessagingOptions> options,
    TimeProvider time,
    ILogger<MessagingCleanup> logger) : BackgroundService
{
    // So one delete never locks more than this many rows.
    private const int Chunk = 1000;

    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!transport.IsConfigured())
        {
            return;
        }

        try
        {
            await Task.Delay(FirstRunDelay, time, stoppingToken);
            while (true)
            {
                try
                {
                    var (outbox, inbox) = RunOnce();
                    MessagingLog.CleanedUp(logger, outbox, inbox);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    MessagingLog.CleanupFailed(logger, exception);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many outbox and inbox rows it deleted.
    internal (int Outbox, int Inbox) RunOnce()
    {
        var cutoff = time.GetUtcNow().UtcDateTime - options.Value.Retention;
        using var scope = scopes.CreateScope();
        var database = store.Resolve(scope.ServiceProvider).Database;

        var outbox = DeleteInChunks(() => database.ExecuteSql($"DELETE FROM outbox_messages WHERE SentAt < {cutoff} LIMIT {Chunk}"));
        var inbox = DeleteInChunks(() => database.ExecuteSql($"DELETE FROM inbox_messages WHERE ReceivedAt < {cutoff} LIMIT {Chunk}"));
        return (outbox, inbox);
    }

    private static int DeleteInChunks(Func<int> delete)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = delete();
            total += deleted;
        }
        while (deleted == Chunk);

        return total;
    }
}