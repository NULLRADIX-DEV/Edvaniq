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

// Runs in the worker after the analyzer. For every document whose topics are reported it asks Knowledge which ids they
// got, and as soon as they are there it asks the AI for the questions of each topic that has no pool yet. The pools,
// an ItemsGenerated per topic and the ready document go out in one save: a worker that stops before leaves the document
// waiting for the next round, and a second worker fails on the status or the pool the first one stored. A topic with a
// pool gets no second one, so unchanged topics cost nothing.
public sealed class QuestionPools(
    IServiceScopeFactory scopes,
    IChatClient ai,
    AiUsageLog usage,
    TimeProvider time,
    ILogger<QuestionPools> logger) : BackgroundService
{
    private const int Batch = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    // How long the AI may take for one answer.
    public TimeSpan AiTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // How long after the analysis Knowledge may take for the topics. Usually it's seconds, this covers a restart of
    // Knowledge or the broker. After that the document fails, a retry gives Knowledge the same time again.
    public TimeSpan TopicsTimeout { get; set; } = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    // Documents that still wait for Knowledge don't count, or the round would ask it again at once.
                    if (await FinishWaitingAsync(stoppingToken) == Batch)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    ContentLog.PoolRoundFailed(logger, exception, Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many documents became ready or failed.
    public async Task<int> FinishWaitingAsync(CancellationToken cancellationToken)
    {
        var finished = 0;
        foreach (var id in await WaitingAsync(cancellationToken))
        {
            if (await FinishAsync(id, cancellationToken))
            {
                finished++;
            }
        }

        return finished;
    }

    // The documents of all users whose topics went out longest ago and that are still in analysis.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents
            .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Analyzing && document.AnalyzedAt != null)
            .OrderBy(document => document.AnalyzedAt)
            .Select(document => document.Id)
            .Take(Batch)
            .ToListAsync(cancellationToken);
    }

    // True if the document became ready or failed, false if it still waits for Knowledge.
    public async Task<bool> FinishAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is not { WaitsForQuestions: true })
        {
            return false;
        }

        var topics = await TopicsAsync(scope.ServiceProvider.GetRequiredService<IKnowledgeTopics>(), documentId, cancellationToken);
        if (topics.Count == 0)
        {
            if (time.GetUtcNow().UtcDateTime - document.WaitsForTopicsSince < TopicsTimeout)
            {
                return false;
            }

            ContentLog.TopicsMissing(logger, documentId);
            document.Fail(Document.TopicsMissing);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var ids = topics.Select(topic => topic.Id).ToList();
        var pooled = await db.QuestionPools.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(pool => ids.Contains(pool.TopicId))
            .Select(pool => pool.TopicId)
            .ToListAsync(cancellationToken);
        var sections = await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.DocumentId == documentId)
            .ToListAsync(cancellationToken);
        var texts = sections.ToDictionary(section => section.Id, section => document.Text.Substring(section.Start, section.Length));

        // Every AI call comes before the one save, nothing holds the database meanwhile. A topic without questions fails
        // the document and nothing of the others goes out.
        var made = new List<(KnowledgeTopic Topic, List<GeneratedItem> Items)>();
        foreach (var topic in topics.Where(topic => !pooled.Contains(topic.Id)))
        {
            var items = await PoolAsync(document, SectionsOf(topics, topic, texts), texts, cancellationToken);
            if (items is null)
            {
                document.Fail(Document.QuestionsFailed);
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }

            made.Add((topic, items));
        }

        var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var (topic, items) in made)
        {
            db.QuestionPools.Add(QuestionPool.For(document, topic.Id, now));
            outbox.Add(new ItemsGenerated(document.OwnerId, document.Id, topic.Position, items, topic.Id));
        }

        document.FinishAnalysis();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Empty while Knowledge has none, and when it can't be asked: the next round asks again, until TopicsTimeout.
    private async Task<IReadOnlyList<KnowledgeTopic>> TopicsAsync(
        IKnowledgeTopics knowledge, Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            return await knowledge.OfDocumentAsync(documentId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The type only: a message could name the address.
            ContentLog.KnowledgeUnavailable(logger, documentId, exception.GetType().Name);
            return [];
        }
    }

    // A topic's own sections. One that is only the roof of its subtopics gets theirs, so every topic has material. Only
    // sections of this document count.
    private static List<Guid> SectionsOf(IReadOnlyList<KnowledgeTopic> topics, KnowledgeTopic topic, Dictionary<Guid, string> texts)
    {
        var own = topic.SectionIds.Count > 0
            ? topic.SectionIds
            : topics.Where(other => other.ParentId == topic.Id).SelectMany(other => other.SectionIds);
        return [.. own.Where(texts.ContainsKey)];
    }

    // The questions of one topic, or null if it got none. An answer without a valid question gets asked again, up to
    // StructuredOutput.DefaultAttempts, since a real model may just have had a bad moment. A provider that fails or takes
    // too long fails at once, retries for that come with #300. Every attempt goes into the cost log.
    private async Task<List<GeneratedItem>?> PoolAsync(
        Document document, IReadOnlyList<Guid> sectionIds, Dictionary<Guid, string> texts, CancellationToken cancellationToken)
    {
        var answer = await ai.AskAsync(
            [
                new ChatMessage(ChatRole.System, QuestionGeneration.System),
                new ChatMessage(ChatRole.User, QuestionGeneration.User(sectionIds.Select(id => texts[id]))),
            ],
            text => QuestionGeneration.Read(text, sectionIds) is { Count: > 0 } drafts ? drafts : null,
            new StructuredRequest(AiTimeout, ContentAi.QuestionsFormat),
            time,
            cancellationToken);
        await usage.RecordAsync(
            document, AiAttempt.Questions, QuestionGeneration.PromptVersion, answer.Tries, ContentAi.ModelOf(ai, null), cancellationToken);
        if (answer.Failure is { } failure)
        {
            ContentLog.QuestionsFailed(logger, failure, document.Id);
            return null;
        }

        if (answer.Value is not { } drafts)
        {
            ContentLog.QuestionsAnswerUnusable(logger, document.Id, answer.Attempts);
            return null;
        }

        var model = ContentAi.ModelOf(ai, answer.ModelId);
        return [.. drafts.Select(draft => new GeneratedItem(
            Guid.NewGuid(), ItemKind.Question, document.Id, draft.SectionId, model, QuestionGeneration.PromptVersion,
            new GeneratedQuestion(draft.Text, draft.Options, draft.Answer, draft.Explanation, draft.Difficulty)))];
    }
}