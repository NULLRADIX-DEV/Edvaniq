using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.LearningEngine.Infrastructure;

// Runs in a consumer without a current user, so the owner filter would throw. The owner comes from the event.
internal sealed class TopicIntroductions(LearningEngineDbContext db) : ITopicIntroductions
{
    public async Task<IReadOnlySet<Guid>> IntroducedAsync(
        string ownerId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken) =>
        (await db.TopicIntroductions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(introduction => introduction.OwnerId == ownerId && topicIds.Contains(introduction.TopicId))
            .Select(introduction => introduction.TopicId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public void Add(TopicIntroduction introduction) => db.TopicIntroductions.Add(introduction);
}