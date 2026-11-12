using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them. So the check for a name
// that is taken only ever sees the user's own topics.
internal sealed class Topics(KnowledgeDbContext db, TopicsChangedEvents changes, ICurrentUser user, TimeProvider time) : ITopics
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

    public async Task<TopicResult> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var topic = await db.Topics.FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken);
        if (topic is null)
        {
            return TopicResult.Fail(TopicError.NotFound);
        }

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