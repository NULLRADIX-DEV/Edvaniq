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
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure;

// Runs in the worker after the analyzer. For every document whose topics are reported it asks Knowledge which ids they
// got, and as soon as they are there it asks the AI for the questions of each topic that has no pool yet. The pools,
// an ItemsGenerated per topic and the ready document go out in one save: a worker that stops before leaves the document
// waiting for the next round, and a second worker fails on the status or the pool the first one stored. A topic with a
// pool gets no second one, so unchanged topics cost nothing.
public sealed class QuestionPools(
    IServiceScopeFactory scopes,
    IChatClient ai,
    TestDoubleAi testDouble,
    IOptions<AnalysisOptions> options,
    AiAnswers answers,
    TimeProvider time,
    ILogger<QuestionPools> logger) : BackgroundService
{
    // Documents per round. A full round means more are waiting, the next one starts without a pause.
    public int Batch { get; set; } = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    // How long the AI may take for one answer.
    public TimeSpan AiTimeout { get; set; } = options.Value.AiTimeout;

    // How long after the analysis Knowledge may take for the topics. Usually it's seconds, this covers a restart of
    // Knowledge or the broker. After that the document fails, a retry gives Knowledge the same time again.
    public TimeSpan TopicsTimeout { get; set; } = options.Value.TopicsTimeout;

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
                    // Documents that still wait for Knowledge don't count, or the round would ask it again at once.
                    if (await FinishWaitingAsync(stoppingToken) == Batch)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    ContentLog.PoolRoundFailed(logger, ContentLog.Describe(exception), Interval);
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

    // The documents still in analysis whose topics went out, the users taking turns (FairOrder), each by the time their
    // topics went out. One postponed after an outage only counts once it's due, and then from that time on. The oldest of
    // every user comes from its own query, so nobody waits behind the cap of another user's many documents.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        using var scope = scopes.CreateScope();
        var due = scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents
            .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Analyzing && document.AnalyzedAt != null)
            .Where(document => document.NextAttemptAt == null || document.NextAttemptAt <= now);
        var oldest = await due
            .Where(document => (document.NextAttemptAt ?? document.AnalyzedAt) == due
                .Where(other => other.OwnerId == document.OwnerId)
                .Min(other => other.NextAttemptAt ?? other.AnalyzedAt))
            .Select(document => new WaitingDocument(document.Id, document.OwnerId, (document.NextAttemptAt ?? document.AnalyzedAt)!.Value))
            .ToListAsync(cancellationToken);
        var rest = await due
            .OrderBy(document => document.NextAttemptAt ?? document.AnalyzedAt)
            .Select(document => new WaitingDocument(document.Id, document.OwnerId, (document.NextAttemptAt ?? document.AnalyzedAt)!.Value))
            .Take(FairOrder.MaxCandidates)
            .ToListAsync(cancellationToken);
        return FairOrder.Of([.. oldest, .. rest], Batch);
    }

    // True if the document became ready or failed, false if it still waits for Knowledge.
    public async Task<bool> FinishAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);
        if (document is not { WaitsForQuestions: true } || document.NextAttemptAt > time.GetUtcNow().UtcDateTime)
        {
            return false;
        }

        var started = time.GetTimestamp();
        var topics = await TopicsAsync(scope.ServiceProvider.GetRequiredService<IKnowledgeTopics>(), documentId, cancellationToken);
        var ids = topics.Select(topic => topic.Id).ToList();
        var pools = await db.QuestionPools.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(pool => ids.Contains(pool.TopicId))
            .ToDictionaryAsync(pool => pool.TopicId, cancellationToken);
        var asked = (await db.PoolMaterials.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(material => ids.Contains(material.TopicId))
                .Select(material => new { material.TopicId, material.SectionId })
                .ToListAsync(cancellationToken))
            .ToLookup(material => material.TopicId, material => material.SectionId);
        var sections = await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.DocumentId == documentId)
            .ToListAsync(cancellationToken);
        var texts = sections.ToDictionary(section => section.Id, section => document.Text.Substring(section.Start, section.Length));

        // No topic yet, or topics none of which has material of this document and none a pool: Knowledge hasn't got them
        // as they will be. Ready with nothing would leave the document without a single question, so it waits like for
        // missing topics, and fails as such after the limit.
        if (topics.Count == 0 || (pools.Count == 0 && topics.All(topic => QuestionRequests.SectionsOf(topics, topic, texts).Count == 0)))
        {
            if (time.GetUtcNow().UtcDateTime - document.WaitsForTopicsSince < TopicsTimeout)
            {
                return false;
            }

            ContentLog.TopicsMissing(logger, documentId);
            document.Fail(DocumentFailures.TopicsMissing);
            await WorkerSave.SaveAsync(db, documentId, "questions", logger, cancellationToken);
            return true;
        }

        // A topic without a pool gets one from its material here. A topic with a pool gets questions on the sections of
        // this document it wasn't asked about yet (#915), none if there are none.
        var needed = new List<Job>();
        foreach (var topic in topics)
        {
            var material = QuestionRequests.SectionsOf(topics, topic, texts);
            if (!pools.TryGetValue(topic.Id, out var pool))
            {
                if (material.Count == 0)
                {
                    ContentLog.TopicWithoutMaterial(logger, topic.Id, documentId);
                    continue;
                }

                needed.Add(new Job(topic, material, null, new HashSet<string>()));
                continue;
            }

            // A pool from before #915 has no material rows: its own document counts as asked, every other as new.
            var before = asked[topic.Id].ToHashSet();
            List<Guid> fresh = before.Count == 0 && pool.DocumentId == documentId ? [] : [.. material.Where(section => !before.Contains(section))];
            if (fresh.Count == 0)
            {
                continue;
            }

            var known = await QuestionRequests.KeysOfAsync(db, pool, cancellationToken);
            if (known.Count >= QuestionPool.MaxAll)
            {
                ContentLog.PoolFull(logger, topic.Id, documentId, known.Count);
                continue;
            }

            needed.Add(new Job(topic, fresh, pool, known));
        }

        // The daily quota before the first AI call: every topic that gets a pool or new material is one generation, all
        // or none. The demo costs none (#411).
        var quota = scope.ServiceProvider.GetRequiredService<IGenerationQuota>();
        if (needed.Count > 0 && document.Origin != DocumentOrigin.Demo && await quota.StateOfAsync(document.OwnerId, cancellationToken) is var state && !state.Allows(needed.Count))
        {
            ContentLog.GenerationQuotaExceeded(logger, documentId, needed.Count, state.Left);
            document.Fail(DocumentFailures.GenerationsOverQuota(needed.Count, state.Left, time.GetUtcNow().UtcDateTime));
            await WorkerSave.SaveAsync(db, documentId, "questions", logger, cancellationToken);
            return true;
        }

        // Every AI call comes before the one save, nothing holds the database meanwhile. A topic without questions fails
        // the document and nothing of the others goes out. An outage postpones it, then all its topics are asked again.
        var made = new List<(Job Job, List<GeneratedItem> Items)>();
        LearnerProfile? profile = null;
        foreach (var job in needed)
        {
            // Asked once per round, only when a topic really needs the AI.
            profile ??= await GoalContext.ProfileOfAsync(document, scope.ServiceProvider.GetRequiredService<IGoalProfiles>(), logger, cancellationToken);
            if (profile is null)
            {
                await WorkerSave.SaveAsync(db, documentId, "questions", logger, cancellationToken);
                return true;
            }

            var system = job.Pool is null
                ? QuestionGeneration.System(profile)
                : QuestionGeneration.ForNewMaterial(profile, job.Room);
            var (items, failure, attempts) = await QuestionRequests.AskAsync(
                document, system, job.Material, texts, AiAttempt.Questions, job.Topic.Id, ai, testDouble, answers, AiTimeout, cancellationToken);
            if (items is null)
            {
                if (failure is not null)
                {
                    AiOutage.Handle(document, AiAttempt.Questions, failure, time.GetUtcNow().UtcDateTime, OutageDelays, logger);
                }
                else
                {
                    ContentLog.QuestionsAnswerUnusable(logger, document.Id, attempts);
                    document.Fail(DocumentFailures.QuestionsFailed);
                }

                await WorkerSave.SaveAsync(db, documentId, "questions", logger, cancellationToken);
                return true;
            }

            made.Add((job, items));
        }

        var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var (job, items) in made)
        {
            var topic = job.Topic;
            if (job.Pool is not { } pool)
            {
                pool = QuestionPool.For(document, topic.Id, now, QuestionRequests.KeysOf(items));
                db.QuestionPools.Add(pool);
                db.PoolMaterials.AddRange(job.Material.Select(section => PoolMaterial.Of(pool, section)));
                if (document.Origin != DocumentOrigin.Demo)
                {
                    quota.Charge(document, topic.Id);
                }

                outbox.Add(new ItemsGenerated(document.OwnerId, document.Id, topic.Position, items, topic.Id));
                continue;
            }

            // Only what the topic doesn't ask yet, each section with the keys of the questions it brought.
            var fresh = QuestionRequests.Fresh(items, job.Known, job.Room);
            db.PoolMaterials.AddRange(job.Material.Select(section => PoolMaterial.Added(
                pool, section, QuestionRequests.KeysOf(fresh.Where(item => item.SectionId == section)))));
            var round = pool.NextRound();
            if (document.Origin != DocumentOrigin.Demo)
            {
                quota.Charge(document, topic.Id, round);
            }

            ContentLog.PoolGotNewMaterial(logger, topic.Id, documentId, job.Material.Count, fresh.Count);
            if (fresh.Count > 0)
            {
                outbox.Add(new ItemsGenerated(document.OwnerId, document.Id, topic.Position, fresh, topic.Id));
            }
        }

        document.FinishAnalysis();
        if (await WorkerSave.SaveAsync(db, documentId, "questions", logger, cancellationToken))
        {
            ContentLog.PoolsMade(
                logger, documentId, made.Count, made.Sum(pool => pool.Items.Count), (long)time.GetElapsedTime(started).TotalMilliseconds);
        }

        return true;
    }

    // What the AI is asked for one topic: a new pool from its material (Pool null), or questions on material of this
    // document that a topic with a pool wasn't asked about yet, at most Room of them, none it Knows.
    private sealed record Job(KnowledgeTopic Topic, List<Guid> Material, QuestionPool? Pool, IReadOnlySet<string> Known)
    {
        public int Room => Math.Min(QuestionPool.PerNewMaterial, QuestionPool.MaxAll - Known.Count);
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
}