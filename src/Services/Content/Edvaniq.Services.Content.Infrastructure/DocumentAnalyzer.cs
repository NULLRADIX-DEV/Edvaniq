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
    AiUsageLog usage,
    TimeProvider time,
    ILogger<DocumentAnalyzer> logger) : BackgroundService
{
    // Documents per round. A full round means more are waiting, the next one starts without a pause.
    private const int Batch = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    // How long the AI may take for one document. A provider that hangs fails the document instead of the worker.
    public TimeSpan AiTimeout { get; set; } = TimeSpan.FromMinutes(2);

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

    // The oldest documents in analysis of all users whose topics are still to come.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents
            .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Analyzing && document.AnalyzedAt == null)
            .OrderBy(document => document.CreatedAt)
            .Select(document => document.Id)
            .Take(Batch)
            .ToListAsync(cancellationToken);
    }

    public async Task AnalyzeAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is not { Status: DocumentStatus.Analyzing, AnalyzedAt: null })
        {
            return;
        }

        var sections = await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.DocumentId == documentId)
            .OrderBy(section => section.Ordinal)
            .ToListAsync(cancellationToken);

        // The AI call comes before the one save, nothing holds the database meanwhile.
        if (await TopicsAsync(document, sections, cancellationToken) is { } topics)
        {
            document.ReportTopics(time.GetUtcNow().UtcDateTime);
            scope.ServiceProvider.GetRequiredService<IEventOutbox>().Add(new DocumentAnalyzed(document.OwnerId, document.Id, topics, document.LearningGoalId));
        }
        else
        {
            document.Fail(Document.AnalysisFailed);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // The topics from the AI, or null if it failed, took too long or gave no usable answer in all attempts. Retries of
    // outages with a pause come with #300, until then the user pastes the text again.
    private async Task<IReadOnlyList<TopicSuggestion>?> TopicsAsync(
        Document document, IReadOnlyList<Section> sections, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids = [.. sections.Select(section => section.Id)];
        var answer = await ai.AskAsync(
            [
                new ChatMessage(ChatRole.System, TopicAnalysis.System),
                new ChatMessage(ChatRole.User, TopicAnalysis.User(sections.Select(section => document.Text.Substring(section.Start, section.Length)))),
            ],
            text => TopicAnalysis.Read(text, ids),
            new StructuredRequest(AiTimeout, ContentAi.TopicsFormat),
            time,
            cancellationToken);
        await usage.RecordAsync(
            document, AiAttempt.Topics, TopicAnalysis.PromptVersion, answer.Tries, ContentAi.ModelOf(ai, null), cancellationToken);
        if (answer.Failure is { } failure)
        {
            ContentLog.AnalysisFailed(logger, failure, document.Id);
        }
        else if (answer.Value is null)
        {
            ContentLog.AnalysisAnswerUnusable(logger, document.Id, answer.Attempts);
        }

        return answer.Value;
    }
}