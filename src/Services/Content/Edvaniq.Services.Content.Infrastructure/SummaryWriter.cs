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
                    LogRoundFailed(exception, Interval);
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
            await FailAsync(db, summary, FailureCode.NoText, cancellationToken);
            return;
        }

        var profile = await ProfileAsync(scope.ServiceProvider.GetRequiredService<IGoalProfiles>(), summary, cancellationToken);
        if (profile is null)
        {
            await FailAsync(db, summary, FailureCode.Unknown, cancellationToken);
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
            if (Handle(summary, failure) is { } code)
            {
                await FailAsync(db, summary, code, cancellationToken);
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
            await FailAsync(db, summary, FailureCode.Unknown, cancellationToken);
            return;
        }

        var used = draft.Used.Select(index => material[index].Position).ToList();
        summary.Finish(draft.Text, used, ContentAi.ModelOf(chat, answer.ModelId), TopicSummaryGeneration.PromptVersion, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
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

    // Like AiOutage for a document: null while it only waits, else the code to fail with.
    private FailureCode? Handle(TopicSummary summary, Exception failure)
    {
        if (failure is AiPausedException or AiBudgetExhaustedException)
        {
            LogStopped(summary.Id, failure.GetType().Name);
            return failure is AiPausedException ? FailureCode.AiUnavailable : FailureCode.LimitReached;
        }

        if (AiFailure.IsTransient(failure) && summary.Postpone(time.GetUtcNow().UtcDateTime, OutageDelays, AiFailure.RetryAfter(failure, time.GetUtcNow().UtcDateTime)))
        {
            LogPostponed(summary.Id, failure.GetType().Name, summary.NextAttemptAt);
            return null;
        }

        LogUnavailable(summary.Id, failure.GetType().Name);
        return FailureCode.AiUnavailable;
    }

    private static async Task FailAsync(ContentDbContext db, TopicSummary summary, FailureCode code, CancellationToken cancellationToken)
    {
        summary.Fail(code);
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A round of summaries failed, the next starts in {Interval}")]
    private partial void LogRoundFailed(Exception exception, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI answered no usable summary {SummaryId} in {Attempts} attempts, it is marked as failed")]
    private partial void LogUnusable(Guid summaryId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planning could not be asked for the learning goal of summary {SummaryId}, it is marked as failed")]
    private partial void LogGoalUnavailable(Guid summaryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI is stopped for summary {SummaryId} ({Reason}), it is marked as failed")]
    private partial void LogStopped(Guid summaryId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI is unavailable for summary {SummaryId} ({Error}), it waits until {NextAttemptAt}")]
    private partial void LogPostponed(Guid summaryId, string error, DateTime? nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI refused summary {SummaryId} ({Error}), it is marked as failed")]
    private partial void LogUnavailable(Guid summaryId, string error);
}