using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Topics(KnowledgeDbContext db) : ITopics
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

    public async Task<Topic?> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var topic = await db.Topics.FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken);
        if (topic is null)
        {
            return null;
        }

        topic.Rename(name);
        await db.SaveChangesAsync(cancellationToken);
        return topic;
    }
}