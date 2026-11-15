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

// Runs in the worker and tops up every pool of a ready document that has fewer than QuestionPool.MinQuestions (#339):
// it asks the AI for the questions still missing to QuestionPool.MaxQuestions and sends only those it hasn't sent
// before, with an ItemsGenerated of the same topic. Pool, event and the round's charge go out in one save. Each round
// with an answer takes one generation of the user's daily quota. While the quota is used up, the budget is reached or the
// operator stopped the AI, nothing is asked and a log line says why. An outage waits like a document (#300), and a pool
// that stays short after QuestionPool.MaxTopUps rounds is left as it is. The document stays ready all along.
public sealed class QuestionTopUps(
    IServiceScopeFactory scopes,
    IChatClient ai,
    TestDoubleAi testDouble,
    IOptions<AnalysisOptions> options,
    AiAnswers answers,
    IModeration moderation,
    TimeProvider time,
    ILogger<QuestionTopUps> logger) : BackgroundService
{
    // Pools per round. A full round means more are waiting, the next one starts without a pause.
    public int Batch { get; set; } = 10;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    // How long a pool waits while the AI is stopped or the budget reached, or Knowledge or Planning can't be asked.
    public static readonly TimeSpan PausedDelay = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan UnreachableDelay = TimeSpan.FromMinutes(1);

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
                    if (await TopUpWaitingAsync(stoppingToken) == Batch)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    ContentLog.TopUpRoundFailed(logger, ContentLog.Describe(exception), Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // Returns how many pools were worked on.
    public async Task<int> TopUpWaitingAsync(CancellationToken cancellationToken)
    {
        var done = 0;
        foreach (var topicId in await WaitingAsync(cancellationToken))
        {
            if (await TopUpAsync(topicId, cancellationToken))
            {
                done++;
            }
        }

        return done;
    }

    // The short pools of ready documents that are due, the users taking turns (FairOrder) by the time they are due.
    public async Task<IReadOnlyList<Guid>> WaitingAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var ready = db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(document => document.Status == DocumentStatus.Ready)
            .Select(document => document.Id);
        var due = await db.QuestionPools.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(pool => pool.Questions < QuestionPool.MinQuestions && !pool.TopUpStopped)
            .Where(pool => pool.NextTopUpAt == null || pool.NextTopUpAt <= now)
            .Where(pool => ready.Contains(pool.DocumentId))
            .OrderBy(pool => pool.NextTopUpAt ?? pool.CreatedAt)
            .Select(pool => new WaitingDocument(pool.TopicId, pool.OwnerId, pool.NextTopUpAt ?? pool.CreatedAt))
            .Take(FairOrder.MaxCandidates)
            .ToListAsync(cancellationToken);
        return FairOrder.Of(due, Batch);
    }

    // True if the pool was worked on, false if it wasn't due or is gone.
    public async Task<bool> TopUpAsync(Guid topicId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ContentDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var pool = await db.QuestionPools.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .SingleOrDefaultAsync(item => item.TopicId == topicId, cancellationToken);
        var document = pool is null
            ? null
            : await db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .SingleOrDefaultAsync(item => item.Id == pool.DocumentId, cancellationToken);
        if (pool is not { WantsTopUp: true } || pool.NextTopUpAt > now || document is not { Status: DocumentStatus.Ready })
        {
            return false;
        }

        if (await BlockedAsync(document, pool, services, now, cancellationToken))
        {
            return await SaveAsync(db, document, cancellationToken);
        }

        var knowledge = services.GetRequiredService<IKnowledgeTopics>();
        IReadOnlyList<KnowledgeTopic> topics;
        LearnerProfile? profile;
        try
        {
            topics = await knowledge.OfDocumentAsync(document.Id, cancellationToken);
            profile = await ProfileOfAsync(document, services.GetRequiredService<IGoalProfiles>(), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The type only: a message could name the address.
            ContentLog.TopUpUnreachable(logger, topicId, exception.GetType().Name, now + UnreachableDelay);
            pool.WaitUntil(now + UnreachableDelay);
            return await SaveAsync(db, document, cancellationToken);
        }

        var topic = topics.SingleOrDefault(item => item.Id == topicId);
        var sections = await db.Sections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.DocumentId == document.Id)
            .ToListAsync(cancellationToken);
        var texts = sections.ToDictionary(section => section.Id, section => document.Text.Substring(section.Start, section.Length));
        var material = topic is null ? [] : QuestionRequests.SectionsOf(topics, topic, texts);
        if (topic is null || material.Count == 0 || profile is null)
        {
            ContentLog.TopUpStopped(logger, topicId, pool.Questions ?? 0, topic is null ? "topic gone" : profile is null ? "goal gone" : "no material");
            pool.Stop();
            return await SaveAsync(db, document, cancellationToken);
        }

        var (items, failure, attempts, _) = await QuestionRequests.AskAsync(
            document, QuestionGeneration.System(profile, pool.Questions ?? 0), material, texts, AiAttempt.TopUp, pool.TopicId, ai, testDouble, answers, moderation,
            AiTimeout, cancellationToken);
        now = time.GetUtcNow().UtcDateTime;
        if (failure is not null)
        {
            Fail(pool, failure, now);
            return await SaveAsync(db, document, cancellationToken);
        }

        var known = await QuestionRequests.KeysOfAsync(db, pool, cancellationToken);
        var fresh = QuestionRequests.Fresh(items ?? [], known, pool.Missing);
        if (!pool.ToppedUp(QuestionRequests.KeysOf(fresh)))
        {
            ContentLog.TopUpStayedShort(logger, topicId, pool.Questions ?? 0, pool.TopUps);
        }
        else
        {
            ContentLog.ToppedUp(logger, topicId, fresh.Count, pool.Questions ?? 0, attempts);
        }

        services.GetRequiredService<IGenerationQuota>().Charge(document, topicId, pool.NextRound());
        if (fresh.Count > 0)
        {
            services.GetRequiredService<IEventOutbox>().Add(new ItemsGenerated(document.OwnerId, document.Id, topic.Position, fresh, topicId));
        }

        return await SaveAsync(db, document, cancellationToken);
    }

    // Nothing is asked while the operator stopped the AI, the month's budget is reached or the user's quota is used up.
    // The test double of a test account calls no provider, so only the quota holds it back.
    private async Task<bool> BlockedAsync(
        Document document, QuestionPool pool, IServiceProvider services, DateTime now, CancellationToken cancellationToken)
    {
        if (!document.UsesTestDouble)
        {
            if (await services.GetRequiredService<IAiSwitch>().IsPausedAsync(cancellationToken))
            {
                ContentLog.TopUpPaused(logger, pool.TopicId, pool.Questions ?? 0, now + PausedDelay);
                pool.WaitUntil(now + PausedDelay);
                return true;
            }

            if ((await services.GetRequiredService<IAiBudget>().StateAsync(cancellationToken)).Exhausted)
            {
                ContentLog.TopUpOverBudget(logger, pool.TopicId, pool.Questions ?? 0, now + PausedDelay);
                pool.WaitUntil(now + PausedDelay);
                return true;
            }
        }

        var quota = await services.GetRequiredService<IGenerationQuota>().StateOfAsync(document.OwnerId, cancellationToken);
        if (!quota.Allows(1))
        {
            ContentLog.TopUpOverQuota(logger, pool.TopicId, pool.Questions ?? 0, quota.ResetsAt);
            pool.WaitUntil(quota.ResetsAt);
            return true;
        }

        return false;
    }

    // Unlike the analysis, a ready document doesn't fail when Planning can't say: null only if the goal is gone, an
    // unreachable Planning throws and the pool waits.
    private static async Task<LearnerProfile?> ProfileOfAsync(Document document, IGoalProfiles profiles, CancellationToken cancellationToken) =>
        document.LearningGoalId is { } goal && goal != Guid.Empty
            ? await profiles.OfAsync(goal, cancellationToken)
            : LearnerProfile.Default;

    private void Fail(QuestionPool pool, Exception failure, DateTime now)
    {
        if (failure is AiPausedException)
        {
            ContentLog.TopUpPaused(logger, pool.TopicId, pool.Questions ?? 0, now + PausedDelay);
            pool.WaitUntil(now + PausedDelay);
        }
        else if (failure is AiBudgetExhaustedException)
        {
            ContentLog.TopUpOverBudget(logger, pool.TopicId, pool.Questions ?? 0, now + PausedDelay);
            pool.WaitUntil(now + PausedDelay);
        }
        else if (!AiFailure.IsTransient(failure))
        {
            ContentLog.TopUpStopped(logger, pool.TopicId, pool.Questions ?? 0, $"refused ({failure.GetType().Name})");
            pool.Stop();
        }
        else if (pool.Postpone(now, OutageDelays, AiFailure.RetryAfter(failure, now)))
        {
            ContentLog.TopUpPostponed(logger, pool.TopicId, failure.GetType().Name, pool.TopUpOutages, pool.NextTopUpAt);
        }
        else
        {
            ContentLog.TopUpStopped(logger, pool.TopicId, pool.Questions ?? 0, $"AI unavailable {OutageDelays.Count + 1} times");
        }
    }

    // The document may go meanwhile, its pool goes with it, then nothing is saved and nothing sent.
    private async Task<bool> SaveAsync(ContentDbContext db, Document document, CancellationToken cancellationToken)
    {
        await WorkerSave.SaveAsync(db, document.Id, "top-up", logger, cancellationToken);
        return true;
    }
}