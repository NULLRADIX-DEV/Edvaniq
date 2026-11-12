using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Assessment.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Assessment.Infrastructure;

public sealed class AttemptRetentionOptions
{
    public const string Section = "AttemptRetention";

    public int Days { get; set; } = Attempt.RetentionDays;

    // Rows deleted per statement.
    public int BatchSize { get; set; } = 500;
}

// Deletes the attempts and block quizzes once their retention is over (#962). Assessment needs an attempt only while it
// is open, while a client may send its answer again and while its block's quiz goes on. What lasts is the learning
// record in LearningEngine, and nothing here is derived from old attempts, so nothing is carried. An answer whose
// AnswerRecorded hasn't left the outbox yet loses nothing: the outbox row holds the whole event, not a reference to the
// attempt. Block quizzes go first, their attempts with them (cascade), then the attempts asked on their own. In portions,
// so no statement holds the table long. Every process may run it, a row deleted twice is simply gone.
public sealed class AttemptRetention(
    IServiceScopeFactory scopes,
    IOptions<AttemptRetentionOptions> options,
    TimeProvider time,
    ILogger<AttemptRetention> logger) : BackgroundService
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
                    var (quizzes, attempts) = RunOnce(stoppingToken);
                    AttemptRetentionLog.Deleted(logger, quizzes, attempts);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    AttemptRetentionLog.Failed(logger, exception);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many block quizzes and single attempts it deleted. Synchronous, as in the outbox dispatcher: MySql.Data's
    // OpenAsync can hang forever.
    public (int Quizzes, int Attempts) RunOnce(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-settings.Days);
        var quizzes = 0;
        var attempts = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
            var ids = db.BlockQuizzes.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(quiz => quiz.CreatedAt < cutoff)
                .OrderBy(quiz => quiz.CreatedAt)
                .Select(quiz => quiz.Id)
                .Take(settings.BatchSize)
                .ToList();
            if (ids.Count == 0)
            {
                break;
            }

            quizzes += db.BlockQuizzes.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Where(quiz => ids.Contains(quiz.Id)).ExecuteDelete();
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
            var ids = db.Attempts.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(attempt => attempt.QuizId == null && attempt.AskedAt < cutoff)
                .OrderBy(attempt => attempt.AskedAt)
                .Select(attempt => attempt.Id)
                .Take(settings.BatchSize)
                .ToList();
            if (ids.Count == 0)
            {
                break;
            }

            attempts += db.Attempts.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Where(attempt => ids.Contains(attempt.Id)).ExecuteDelete();
        }

        return (quizzes, attempts);
    }
}

internal static partial class AttemptRetentionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Retention deleted {Quizzes} expired block quizzes with their attempts and {Attempts} expired single attempts")]
    public static partial void Deleted(ILogger logger, int quizzes, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retention of attempts failed")]
    public static partial void Failed(ILogger logger, Exception exception);
}