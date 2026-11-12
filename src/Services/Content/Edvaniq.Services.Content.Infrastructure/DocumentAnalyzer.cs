using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// Runs in the worker after the splitter and asks the AI for the topics of every document in analysis. DocumentAnalyzed
// and the time of the analysis go out in the same save, so a document is reported exactly once: a worker that stops
// before leaves it for the next round, and a second worker on the same document fails on the time the first one set.
// The document stays in analysis, QuestionPools makes it ready once Knowledge has given the topics their ids.
public sealed class DocumentAnalyzer(
    IServiceScopeFactory scopes,
    IChatClient ai,
    TestDoubleAi testDouble,
    AiUsageLog usage,
    TimeProvider time,
    ILogger<DocumentAnalyzer> logger) : BackgroundService
{
    // Documents per round. A full round means more are waiting, the next one starts without a pause.
    public int Batch { get; set; } = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    // How long the AI may take for one document. A provider that hangs fails the document instead of the worker.
    public TimeSpan AiTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // How long a document waits after each outage of the AI, the worker goes on with the others meanwhile.
    public IReadOnlyList<TimeSpan> OutageDelays { get; set; } = Document.OutageDelays;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    if (await AnalyzeWaitingAsync(stoppingToken) == Batch)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    ContentLog.AnalysisRoundFailed(logger, exception, Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many documents it took on.
    public async Task<int> AnalyzeWaitingAsync(CancellationToken cancellationToken)
    {
        var waiting = await WaitingAsync(cancellationToken);
        foreach (var id in waiting)
        {
            await AnalyzeAsync(id, cancellationToken);
        }

        return waiting.Count;
    }

    // The documents in analysis of all users whose topics are still to come, the users taking turns (FairOrder), each
    // oldest first. One postponed after an outage only counts once it's due, and then from that time on. The oldest of
    // every user comes from its own query, so nobody waits behind the cap of another user's many documents.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        using var scope = scopes.CreateScope();
        var due = scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents
            .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Analyzing && document.AnalyzedAt == null)
            .Where(document => document.NextAttemptAt == null || document.NextAttemptAt <= now);
        var oldest = await due
            .Where(document => (document.NextAttemptAt ?? document.CreatedAt) == due
                .Where(other => other.OwnerId == document.OwnerId)
                .Min(other => other.NextAttemptAt ?? other.CreatedAt))
            .Select(document => new WaitingDocument(document.Id, document.OwnerId, document.NextAttemptAt ?? document.CreatedAt))
            .ToListAsync(cancellationToken);
        var rest = await due
            .OrderBy(document => document.NextAttemptAt ?? document.CreatedAt)
            .Select(document => new WaitingDocument(document.Id, document.OwnerId, document.NextAttemptAt ?? document.CreatedAt))
            .Take(FairOrder.MaxCandidates)
            .ToListAsync(cancellationToken);
        return FairOrder.Of([.. oldest, .. rest], Batch);
    }

    public async Task AnalyzeAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is not { Status: DocumentStatus.Analyzing, AnalyzedAt: null } || document.NextAttemptAt > time.GetUtcNow().UtcDateTime)
        {
            return;
        }

        var sections = await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.DocumentId == documentId)
            .OrderBy(section => section.Ordinal)
            .ToListAsync(cancellationToken);

        var profile = await GoalContext.ProfileOfAsync(document, scope.ServiceProvider.GetRequiredService<IGoalProfiles>(), logger, cancellationToken);
        if (profile is null)
        {
            await WorkerSave.SaveAsync(db, documentId, "analysis", logger, cancellationToken);
            return;
        }

        // The AI call comes before the one save, nothing holds the database meanwhile.
        var answer = await TopicsAsync(document, profile, sections, cancellationToken);
        if (answer.Value is { } topics)
        {
            document.ReportTopics(time.GetUtcNow().UtcDateTime);
            scope.ServiceProvider.GetRequiredService<IEventOutbox>().Add(new DocumentAnalyzed(document.OwnerId, document.Id, topics, document.LearningGoalId));
        }
        else if (answer.Failure is { } failure)
        {
            AiOutage.Handle(document, AiAttempt.Topics, failure, time.GetUtcNow().UtcDateTime, OutageDelays, logger);
        }
        else
        {
            document.Fail(Document.AnalysisFailed);
        }

        await WorkerSave.SaveAsync(db, documentId, "analysis", logger, cancellationToken);
    }

    // The answer of the AI with the topics, if any. Every attempt goes into the cost log.
    private async Task<StructuredAnswer<IReadOnlyList<TopicSuggestion>>> TopicsAsync(
        Document document, LearnerProfile profile, IReadOnlyList<Section> sections, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = [.. sections.Select(section => section.Id)];
        var chat = ContentAi.For(document, ai, testDouble);
        var answer = await chat.AskAsync(
            [
                new ChatMessage(ChatRole.System, TopicAnalysis.System(profile)),
                new ChatMessage(ChatRole.User, TopicAnalysis.User(sections.Select(section => document.Text.Substring(section.Start, section.Length)))),
            ],
            text => TopicAnalysis.Read(text, ids),
            new StructuredRequest(AiTimeout, ContentAi.TopicsFormat),
            time,
            cancellationToken);
        await usage.RecordAsync(
            document, AiAttempt.Topics, TopicAnalysis.PromptVersion, answer.Tries, ContentAi.ModelOf(chat, null), cancellationToken);
        if (answer is { Value: null, Failure: null })
        {
            ContentLog.AnalysisAnswerUnusable(logger, document.Id, answer.Attempts);
        }

        return answer;
    }
}