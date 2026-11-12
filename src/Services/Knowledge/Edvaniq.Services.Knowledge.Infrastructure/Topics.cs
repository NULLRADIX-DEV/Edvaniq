using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them. So the check for a name
// that is taken only ever sees the user's own topics.
// Every change reads the TopicSet of the goal first and only then checks names: a change that commits in between bumps
// the version this save then loses on, so no two changes pass each other on stale reads (#322).
internal sealed class Topics(KnowledgeDbContext db, TopicsChangedEvents changes, ITopicState state, ICurrentUser user, TimeProvider time) : ITopics
{
    public async Task<IReadOnlyList<Topic>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken)
    {
        var topics = db.Topics.AsNoTracking();
        if (learningGoalId is { } goal)
        {
            topics = topics.Where(topic => topic.LearningGoalId == goal);
        }

        return await topics.OrderBy(topic => topic.CreatedAt).ThenBy(topic => topic.Position).ThenBy(topic => topic.Name).ToListAsync(cancellationToken);
    }

    public Task<Topic?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Topics.AsNoTracking().FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KeyTerm>?> TermsAsync(Guid topicId, CancellationToken cancellationToken)
    {
        if (!await db.Topics.AnyAsync(topic => topic.Id == topicId, cancellationToken))
        {
            return null;
        }

        var terms = await db.KeyTerms.AsNoTracking()
            .Where(term => term.TopicId == topicId)
            .OrderBy(term => term.CreatedAt)
            .ThenBy(term => term.Position)
            .ToListAsync(cancellationToken);
        return [.. terms.DistinctBy(term => KeyTerm.KeyOf(term.Term))];
    }

    public async Task<IReadOnlyList<TopicSection>?> SectionsAsync(Guid topicId, CancellationToken cancellationToken)
    {
        if (!await db.Topics.AnyAsync(topic => topic.Id == topicId, cancellationToken))
        {
            return null;
        }

        return await db.TopicSections.AsNoTracking()
            .Where(section => section.TopicId == topicId)
            .OrderBy(section => section.DocumentId)
            .ThenBy(section => section.SectionId)
            .ToListAsync(cancellationToken);
    }

    public async Task<TopicResult> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var topic = await db.Topics.FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken);
        if (topic is null)
        {
            return TopicResult.Fail(TopicError.NotFound);
        }

        await state.TouchAsync(topic.OwnerId, topic.LearningGoalId, cancellationToken);
        if (await IsTakenAsync(topic.LearningGoalId, topic.ParentId, name, topic.Id, cancellationToken))
        {
            return TopicResult.Fail(TopicError.NameTaken);
        }

        topic.Rename(name);
        await changes.AnnounceAsync(topic.OwnerId, topic.LearningGoalId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TopicResult.Ok(topic);
    }

    public async Task<TopicResult> AddAsync(Guid learningGoalId, Guid? parentId, string name, CancellationToken cancellationToken)
    {
        var parent = parentId is { } id ? await db.Topics.FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken) : null;
        if (parentId is not null && parent is null)
        {
            return TopicResult.Fail(TopicError.NotFound);
        }

        if (parent is { IsSubtopic: true })
        {
            return TopicResult.Fail(TopicError.ParentIsSubtopic);
        }

        var goal = parent?.LearningGoalId ?? learningGoalId;
        await state.TouchAsync(user.Id, goal, cancellationToken);
        if (await IsTakenAsync(goal, parentId, name, except: null, cancellationToken))
        {
            return TopicResult.Fail(TopicError.NameTaken);
        }

        // After all topics there are, like a new one from an analysis.
        var now = time.GetUtcNow().UtcDateTime;
        var topic = parent is null ? Topic.Create(user.Id, goal, name, now) : parent.AddSubtopic(name, now);
        db.Topics.Add(topic);
        await changes.AnnounceAsync(topic.OwnerId, goal, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TopicResult.Ok(topic);
    }

    public async Task<TopicResult> SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken)
    {
        var topic = await db.Topics.FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken);
        if (topic is null)
        {
            return TopicResult.Fail(TopicError.NotFound);
        }

        await state.TouchAsync(topic.OwnerId, topic.LearningGoalId, cancellationToken);
        if (!hidden && topic.ParentId is { } parentId
            && await db.Topics.AnyAsync(parent => parent.Id == parentId && parent.Hidden, cancellationToken))
        {
            return TopicResult.Fail(TopicError.ParentHidden);
        }

        if (topic.Hidden == hidden)
        {
            return TopicResult.Ok(topic);
        }

        if (hidden)
        {
            topic.Hide();
        }
        else
        {
            topic.Show();
        }

        await changes.AnnounceAsync(topic.OwnerId, topic.LearningGoalId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TopicResult.Ok(topic);
    }

    // Hidden topics count: the user brings one back rather than making it twice.
    private async Task<bool> IsTakenAsync(Guid learningGoalId, Guid? parentId, string name, Guid? except, CancellationToken cancellationToken)
    {
        var siblings = await db.Topics.AsNoTracking()
            .Where(topic => topic.LearningGoalId == learningGoalId && topic.ParentId == parentId && topic.Id != except)
            .ToListAsync(cancellationToken);
        return siblings.Any(topic => topic.IsCalled(name));
    }
}