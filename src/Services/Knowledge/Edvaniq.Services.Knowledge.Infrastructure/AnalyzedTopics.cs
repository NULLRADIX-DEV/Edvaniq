using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Application;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// Runs in a consumer without a current user, so the owner filter would throw: the query drops it and names the owner.
internal sealed class AnalyzedTopics(KnowledgeDbContext db) : IAnalyzedTopics
{
    public Task<bool> ExistForAsync(string ownerId, Guid documentId, CancellationToken cancellationToken) =>
        db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .AnyAsync(topic => topic.OwnerId == ownerId && topic.DocumentId == documentId, cancellationToken);

    public void Add(TopicImport import)
    {
        db.Topics.AddRange(import.Topics);
        db.TopicSections.AddRange(import.Sections);
    }
}