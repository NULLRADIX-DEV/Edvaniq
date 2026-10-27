using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// Runs in the worker after the splitter and asks the AI for the topics of every document in analysis. The new status and
// DocumentAnalyzed go out in the same save, so a document is reported exactly once: a worker that stops before leaves it
// in analysis for the next round, and a second worker on the same document fails on the status it changed first.
public sealed class DocumentAnalyzer(
    IServiceScopeFactory scopes,
    IChatClient ai,
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

    // The oldest documents in analysis of all users.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents
            .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Analyzing)
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
        if (document?.Status != DocumentStatus.Analyzing)
        {
            return;
        }

        var sections = await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.DocumentId == documentId)
            .OrderBy(section => section.Ordinal)
            .ToListAsync(cancellationToken);
        if (await TopicsAsync(document, sections, cancellationToken) is { } topics)
        {
            document.FinishAnalysis();
            scope.ServiceProvider.GetRequiredService<IEventOutbox>()
                .Add(new DocumentAnalyzed(document.OwnerId, document.Id, topics));
        }
        else
        {
            document.Fail(Document.AnalysisFailed);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // The topics from the AI, or null if it failed, took too long or gave an answer that is no use. Retries with a pause
    // come with the handling of provider outages (#300), until then the user pastes the text again.
    private async Task<IReadOnlyList<TopicSuggestion>?> TopicsAsync(
        Document document, IReadOnlyList<Section> sections, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(AiTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var response = await ai.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, TopicAnalysis.System),
                    new ChatMessage(ChatRole.User, TopicAnalysis.User(sections.Select(section => document.Text.Substring(section.Start, section.Length)))),
                ],
                cancellationToken: linked.Token);
            var topics = TopicAnalysis.Read(response.Text, [.. sections.Select(section => section.Id)]);
            if (topics is null)
            {
                ContentLog.AnalysisAnswerUnusable(logger, document.Id);
            }

            return topics;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ContentLog.AnalysisFailed(logger, exception, document.Id);
            return null;
        }
    }
}
