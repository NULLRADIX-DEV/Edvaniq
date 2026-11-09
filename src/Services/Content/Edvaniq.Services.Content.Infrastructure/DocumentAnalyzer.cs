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

// Runs in the worker after the splitter and asks the AI for the topics of every document in analysis, then for the
// questions of each topic. The new status, DocumentAnalyzed and an ItemsGenerated per topic go out in the same save, so a
// document is reported exactly once: a worker that stops before leaves it in analysis for the next round, and a second
// worker on the same document fails on the status it changed first.
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
        // Every AI call comes before the one save, nothing holds the database meanwhile. A worker that stops in between
        // leaves the document in analysis with nothing of it saved.
        var texts = sections.ToDictionary(section => section.Id, section => document.Text.Substring(section.Start, section.Length));
        if (await TopicsAsync(document, sections, texts, cancellationToken) is not { } topics)
        {
            document.Fail(Document.AnalysisFailed);
        }
        else if (await QuestionsAsync(document, topics, texts, cancellationToken) is not { } pools)
        {
            document.Fail(Document.QuestionsFailed);
        }
        else
        {
            document.FinishAnalysis();
            var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
            outbox.Add(new DocumentAnalyzed(document.OwnerId, document.Id, topics));
            foreach (var (position, items) in pools.Index())
            {
                outbox.Add(new ItemsGenerated(document.OwnerId, document.Id, position, items));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // The topics from the AI, or null if it failed, took too long or gave an answer that is no use. Retries with a pause
    // come with the handling of provider outages (#300), until then the user pastes the text again.
    private async Task<IReadOnlyList<TopicSuggestion>?> TopicsAsync(
        Document document, IReadOnlyList<Section> sections, Dictionary<Guid, string> texts, CancellationToken cancellationToken)
    {
        try
        {
            var response = await AskAsync(
                TopicAnalysis.System, TopicAnalysis.User(sections.Select(section => texts[section.Id])), cancellationToken);
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

    // The questions of every topic in the order of the topics, or null if a topic got none. An answer without a valid
    // question gets one more try, since a real model may just have had a bad moment. A provider that fails or takes too
    // long fails at once, retries for that come with #300.
    private async Task<List<List<GeneratedItem>>?> QuestionsAsync(
        Document document, IReadOnlyList<TopicSuggestion> topics, Dictionary<Guid, string> texts, CancellationToken cancellationToken)
    {
        var pools = new List<List<GeneratedItem>>();
        try
        {
            foreach (var (position, topic) in topics.Index())
            {
                if (await PoolAsync(document, SectionsOf(topics, position, topic), texts, cancellationToken) is not { } pool)
                {
                    ContentLog.QuestionsAnswerUnusable(logger, document.Id, position);
                    return null;
                }

                pools.Add(pool);
            }

            return pools;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ContentLog.QuestionsFailed(logger, exception, document.Id);
            return null;
        }
    }

    private async Task<List<GeneratedItem>?> PoolAsync(
        Document document, IReadOnlyList<Guid> sectionIds, Dictionary<Guid, string> texts, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await AskAsync(
                QuestionGeneration.System, QuestionGeneration.User(sectionIds.Select(id => texts[id])), cancellationToken);
            if (QuestionGeneration.Read(response.Text, sectionIds) is { Count: > 0 } drafts)
            {
                var model = ModelOf(response);
                return [.. drafts.Select(draft => new GeneratedItem(
                    Guid.NewGuid(), ItemKind.Question, document.Id, draft.SectionId, model, QuestionGeneration.PromptVersion,
                    new GeneratedQuestion(draft.Text, draft.Options, draft.Answer, draft.Explanation, draft.Difficulty)))];
            }
        }

        return null;
    }

    // A topic's own sections. One that is only the roof of its subtopics gets theirs, so every topic has material.
    private static List<Guid> SectionsOf(IReadOnlyList<TopicSuggestion> topics, int position, TopicSuggestion topic) =>
        topic.SectionIds.Count > 0
            ? [.. topic.SectionIds]
            : [.. topics.Where(other => other.Parent == position).SelectMany(other => other.SectionIds)];

    // The model that answered. Some providers leave it out of the answer, then it is the configured one, never empty.
    private string ModelOf(ChatResponse response) =>
        new[] { response.ModelId, ai.GetService<ChatClientMetadata>()?.DefaultModelId, ai.GetService<ChatClientMetadata>()?.ProviderName }
            .FirstOrDefault(model => !string.IsNullOrWhiteSpace(model)) ?? "unknown";

    private async Task<ChatResponse> AskAsync(string system, string user, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(AiTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await ai.GetResponseAsync(
            [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user)],
            cancellationToken: linked.Token);
    }
}