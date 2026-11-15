using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure;

// Runs in the worker, the one process that reaches the AI (#344). Takes the requested summaries oldest first, asks the
// AI once for each and stores the text, or postpones it while the AI is away like a document, or fails it with a code
// the client words. Every call goes through AiAnswers under the first document of the material.
public sealed partial class SummaryWriter(
    IServiceScopeFactory scopes,
    IChatClient ai,
    TestDoubleAi testDouble,
    IOptions<AnalysisOptions> options,
    AiAnswers answers,
    TimeProvider time,
    ILogger<SummaryWriter> logger) : BackgroundService
{
    public int Batch { get; set; } = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    public TimeSpan AiTimeout { get; set; } = options.Value.AiTimeout;

    public IReadOnlyList<TimeSpan> OutageDelays { get; set; } = Document.OutageDelays;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    if (await WriteWaitingAsync(stoppingToken) == Batch)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogRoundFailed(ContentLog.Describe(exception), Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // How many summaries it took.
    public async Task<int> WriteWaitingAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        List<Guid> waiting;
        using (var scope = scopes.CreateScope())
        {
            waiting = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().TopicSummaries
                .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(summary => summary.Status == SummaryStatus.Pending && (summary.NextAttemptAt == null || summary.NextAttemptAt <= now))
                .OrderBy(summary => summary.NextAttemptAt ?? summary.CreatedAt)
                .Select(summary => summary.Id)
                .Take(Batch)
                .ToListAsync(cancellationToken);
        }

        foreach (var id in waiting)
        {
            await WriteAsync(id, cancellationToken);
        }

        return waiting.Count;
    }

    public async Task WriteAsync(Guid summaryId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var summary = await db.TopicSummaries.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Include(item => item.Sections)
            .SingleOrDefaultAsync(item => item.Id == summaryId, cancellationToken);
        if (summary is not { Status: SummaryStatus.Pending })
        {
            return;
        }

        var started = time.GetTimestamp();
        var sectionIds = summary.Sections.Select(section => section.SectionId).ToList();
        var documentIds = summary.Sections.Select(section => section.DocumentId).Distinct().ToList();
        var stored = await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => sectionIds.Contains(section.Id)).ToDictionaryAsync(section => section.Id, cancellationToken);
        var documents = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => documentIds.Contains(document.Id)).ToDictionaryAsync(document => document.Id, cancellationToken);
        var material = summary.Sections
            .Where(section => stored.ContainsKey(section.SectionId) && documents.ContainsKey(section.DocumentId))
            .Select(section => (Position: section.Position, Text: Text(documents[section.DocumentId], stored[section.SectionId])))
            .ToList();
        if (material.Count == 0)
        {
            LogNoMaterial(summary.Id);
            await FailAsync(db, summary, DocumentFailures.SummaryNoMaterial, cancellationToken);
            return;
        }

        // The demo answers were written for the default profile (#411).
        var profile = documents.Values.All(document => document.Origin == DocumentOrigin.Demo)
            ? LearnerProfile.Default
            : await ProfileAsync(scope.ServiceProvider.GetRequiredService<IGoalProfiles>(), summary, cancellationToken);
        if (profile is null)
        {
            await FailAsync(db, summary, DocumentFailures.GoalUnavailable, cancellationToken);
            return;
        }

        var first = documents[summary.Sections.First(section => documents.ContainsKey(section.DocumentId)).DocumentId];
        var materialWords = material.Sum(item => TopicSummaryGeneration.WordCount(item.Text));
        // Through AiAnswers like every AI call of a document (#306): the same material asked again costs nothing. The first
        // document stands for the material there and in the cost log, and decides about the test double.
        var chat = ContentAi.For(first, ai, testDouble);
        var answer = await answers.AskAsync(
            first,
            AiAttempt.Summary,
            TopicSummaryGeneration.PromptVersion,
            AiReference.Summary(summary.Id),
            chat,
            [
                new ChatMessage(ChatRole.System, TopicSummaryGeneration.System(profile, summary.Length)),
                new ChatMessage(ChatRole.User, TopicSummaryGeneration.User(material.Select(item => item.Text))),
            ],
            text => TopicSummaryGeneration.Read(text, material.Count, summary.Length, materialWords),
            new StructuredRequest(AiTimeout, ContentAi.SummaryFormat),
            cancellationToken);

        if (answer.Failure is { } failure)
        {
            if (Handle(summary, failure) is { } reason)
            {
                await FailAsync(db, summary, reason, cancellationToken);
            }
            else
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        if (answer.Value is not { } draft)
        {
            LogUnusable(summary.Id, answer.Attempts);
            await FailAsync(db, summary, DocumentFailures.SummaryFailed, cancellationToken);
            return;
        }

        var used = draft.Used.Select(index => material[index].Position).ToList();
        summary.Finish(draft.Text, used, ContentAi.ModelOf(chat, answer.ModelId), TopicSummaryGeneration.PromptVersion, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        LogReady(
            summary.Id, summary.Length, used.Count, (long)time.GetElapsedTime(started).TotalMilliseconds,
            (long)(summary.ReadyAt!.Value - summary.CreatedAt).TotalSeconds);
    }

    private static string Text(Document document, Section section) => document.Text.Substring(section.Start, section.Length);

    private async Task<LearnerProfile?> ProfileAsync(IGoalProfiles profiles, TopicSummary summary, CancellationToken cancellationToken)
    {
        if (summary.LearningGoalId is not { } goal || goal == Guid.Empty)
        {
            return LearnerProfile.Default;
        }

        try
        {
            return await profiles.OfAsync(goal, cancellationToken);
        }
        catch (LearningGoalUnavailableException)
        {
            LogGoalUnavailable(summary.Id);
            return null;
        }
    }

    // Like AiOutage for a document: null while it only waits, else the reason to fail with.
    private DocumentFailure? Handle(TopicSummary summary, Exception failure)
    {
        if (failure is AiPausedException or AiBudgetExhaustedException)
        {
            LogStopped(summary.Id, failure.GetType().Name);
            return failure is AiPausedException ? DocumentFailures.AiPaused : DocumentFailures.AiBudgetReached;
        }

        if (AiFailure.IsTransient(failure) && summary.Postpone(time.GetUtcNow().UtcDateTime, OutageDelays, AiFailure.RetryAfter(failure, time.GetUtcNow().UtcDateTime)))
        {
            LogPostponed(summary.Id, failure.GetType().Name, summary.NextAttemptAt);
            return null;
        }

        LogUnavailable(summary.Id, failure.GetType().Name);
        return DocumentFailures.AiUnavailable;
    }

    private static async Task FailAsync(ContentDbContext db, TopicSummary summary, DocumentFailure failure, CancellationToken cancellationToken)
    {
        summary.Fail(failure);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Fixed event ids (#417), so the way of a summary can be found in the log. Never its text or material.
    // The type and place only: a parser or the provider may quote the material in a message.
    [LoggerMessage(EventId = 33950, Level = LogLevel.Error, Message = "A round of summaries failed ({Error}), the next starts in {Interval}")]
    private partial void LogRoundFailed(string error, TimeSpan interval);

    [LoggerMessage(EventId = 33951, Level = LogLevel.Information, Message = "Summary {SummaryId} ({Length}) is ready: {Sources} sources, written in {DurationMs} ms, {WaitedSeconds} s after it was asked for")]
    private partial void LogReady(Guid summaryId, SummaryLength length, int sources, long durationMs, long waitedSeconds);

    [LoggerMessage(EventId = 33952, Level = LogLevel.Warning, Message = "The AI answered no usable summary {SummaryId} in {Attempts} attempts, it is marked as failed")]
    private partial void LogUnusable(Guid summaryId, int attempts);

    [LoggerMessage(EventId = 33953, Level = LogLevel.Warning, Message = "Planning could not be asked for the learning goal of summary {SummaryId}, it is marked as failed")]
    private partial void LogGoalUnavailable(Guid summaryId);

    [LoggerMessage(EventId = 33954, Level = LogLevel.Warning, Message = "The AI is stopped for summary {SummaryId} ({Reason}), it is marked as failed")]
    private partial void LogStopped(Guid summaryId, string reason);

    [LoggerMessage(EventId = 33955, Level = LogLevel.Warning, Message = "The AI is unavailable for summary {SummaryId} ({Error}), it waits until {NextAttemptAt}")]
    private partial void LogPostponed(Guid summaryId, string error, DateTime? nextAttemptAt);

    [LoggerMessage(EventId = 33956, Level = LogLevel.Error, Message = "The AI refused summary {SummaryId} ({Error}), it is marked as failed")]
    private partial void LogUnavailable(Guid summaryId, string error);

    [LoggerMessage(EventId = 33957, Level = LogLevel.Warning, Message = "The material of summary {SummaryId} is gone, it is marked as failed")]
    private partial void LogNoMaterial(Guid summaryId);
}