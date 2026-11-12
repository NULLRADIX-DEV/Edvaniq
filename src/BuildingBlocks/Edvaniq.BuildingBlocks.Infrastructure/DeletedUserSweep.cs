using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// A handler that checked for a deleted user just before the deletion was stored can still save afterwards (#944). Once
// MessagingOptions.DeletedUserSweepAfter has passed, the sweep looks for rows of each new deleted user and runs the
// service's whole deletion again (IUserDeletion, files included). It knows the user only by the hash, so it hashes the
// owners of all rows. That only happens in the minutes after a deletion. Every process may run it, a second run finds
// nothing.
public sealed partial class DeletedUserSweep<TContext>(
    IServiceScopeFactory scopes,
    IOptions<MessagingOptions> options,
    TimeProvider time,
    ILogger<DeletedUserSweep<TContext>> logger) : BackgroundService
    where TContext : ServiceDbContext
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Returns how many users still had rows.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var due = time.GetUtcNow().UtcDateTime - options.Value.DeletedUserSweepAfter;

        // Sync on purpose, MySql.Data's OpenAsync can hang (MySqlHealthCheck).
        var deleted = db.Set<DeletedUser>().Where(user => !user.Swept && user.DeletedAt <= due).ToList();
        if (deleted.Count == 0)
        {
            return 0;
        }

        var hashes = deleted.Select(user => user.Hash).ToHashSet(StringComparer.Ordinal);
        var leftovers = UserDataRemover<TContext>.OwnerIds(db).Where(owner => hashes.Contains(DeletedUser.HashOf(owner))).ToList();
        var deletion = scope.ServiceProvider.GetRequiredService<IUserDeletion>();
        foreach (var owner in leftovers)
        {
            await deletion.DeleteAsync(owner, cancellationToken);
        }

        foreach (var user in deleted)
        {
            user.MarkSwept();
        }

        db.SaveChanges();
        if (leftovers.Count > 0)
        {
            LogLeftovers(logger, leftovers.Count);
        }

        return leftovers.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(Interval, time, stoppingToken);
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogFailed(logger, exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sweep removed rows that {Users} deleted users got after their deletion")]
    private static partial void LogLeftovers(ILogger logger, int users);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sweep for rows of deleted users failed, trying again in a minute")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}