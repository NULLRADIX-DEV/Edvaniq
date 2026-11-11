using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Application;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// Runs without a current user, in a consumer or for Content's worker, so the owner filter would throw: the queries
// drop it and name the owner or the document.
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

    public async Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var topics = await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.DocumentId == documentId)
            .OrderBy(topic => topic.Position)
            .Select(topic => new { topic.Id, topic.ParentId, topic.Position })
            .ToListAsync(cancellationToken);
        var ids = topics.Select(topic => topic.Id).ToList();
        var sections = (await db.TopicSections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(section => ids.Contains(section.TopicId))
                .Select(section => new { section.TopicId, section.SectionId })
                .ToListAsync(cancellationToken))
            .ToLookup(section => section.TopicId, section => section.SectionId);

        return [.. topics.Select(topic => new DocumentTopic(topic.Id, topic.ParentId, topic.Position, [.. sections[topic.Id]]))];
    }
}