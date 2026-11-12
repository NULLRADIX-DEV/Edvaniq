using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.LearningEngine.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.LearningEngine.Infrastructure;

public sealed class LearningRecordRetentionOptions
{
    public const string Section = "LearningRecordRetention";

    public int Months { get; set; } = LearningRecord.RetentionMonths;

    // Records of one topic folded and deleted per save.
    public int BatchSize { get; set; } = 500;
}

// Deletes learning records once their retention is over (Q27, #365). Before a record goes, it's folded into the carry of
// its topic, so mastery, state and last answer stay as they were. One portion at a time: the oldest expired records of
// one topic, folded, deleted and the carry written in one save, so a restart halfway loses nothing. Every process may
// run it: two that fold the same records at once clash on the carry's key or token, the loser's save rolls back.
public sealed class LearningRecordRetention(
    IServiceScopeFactory scopes,
    IOptions<LearningRecordRetentionOptions> options,
    TimeProvider time,
    ILogger<LearningRecordRetention> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, time, stoppingToken);
            while (true)
            {
                try
                {
                    RetentionLog.Folded(logger, RunOnce(stoppingToken));
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    RetentionLog.Failed(logger, exception);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many records it deleted. The database calls are synchronous, as in the outbox dispatcher: MySql.Data's
    // OpenAsync can hang forever.
    public int RunOnce(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = time.GetUtcNow().UtcDateTime.AddMonths(-settings.Months);
        var total = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
            var records = db.LearningRecords.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]);
            var next = records
                .Where(record => record.AnsweredAt < cutoff)
                .OrderBy(record => record.AnsweredAt)
                .ThenBy(record => record.AnswerId)
                .Select(record => new { record.OwnerId, record.TopicId })
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }

            var expired = records
                .Where(record => record.OwnerId == next.OwnerId && record.TopicId == next.TopicId && record.AnsweredAt < cutoff)
                .OrderBy(record => record.AnsweredAt)
                .ThenBy(record => record.AnswerId)
                .Take(settings.BatchSize)
                .ToList();
            var introduction = db.TopicIntroductions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AsNoTracking()
                .SingleOrDefault(item => item.OwnerId == next.OwnerId && item.TopicId == next.TopicId);
            var carry = db.MasteryCarries.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .SingleOrDefault(item => item.OwnerId == next.OwnerId && item.TopicId == next.TopicId);

            var folded = MasteryCarry.Fold(carry, next.OwnerId, next.TopicId, expired, introduction);
            if (carry is null)
            {
                db.MasteryCarries.Add(folded);
            }

            db.LearningRecords.RemoveRange(expired);
            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateException exception)
            {
                // Another process folded the same records first. What's left waits for the next run.
                RetentionLog.Clashed(logger, exception);
                break;
            }

            total += expired.Count;
        }

        return total;
    }
}

internal static partial class RetentionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Retention deleted {Count} expired learning records")]
    public static partial void Folded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention stopped early, another process was folding the same records")]
    public static partial void Clashed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retention of learning records failed")]
    public static partial void Failed(ILogger logger, Exception exception);
}