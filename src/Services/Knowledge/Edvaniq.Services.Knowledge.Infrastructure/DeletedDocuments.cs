using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// Runs in consumers without a current user, so the queries drop the owner filter and name the owner.
internal sealed class DeletedDocuments(KnowledgeDbContext db) : IDeletedDocuments
{
    public Task<bool> IsDeletedAsync(string ownerId, Guid documentId, CancellationToken cancellationToken) =>
        db.DeletedDocuments.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .AnyAsync(document => document.OwnerId == ownerId && document.DocumentId == documentId, cancellationToken);

    public async Task<IReadOnlyList<Topic>> RemoveAsync(string ownerId, Guid documentId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        if (await IsDeletedAsync(ownerId, documentId, cancellationToken))
        {
            return [];
        }

        db.DeletedDocuments.Add(DeletedDocument.Of(ownerId, documentId, deletedAt));
        var sections = await db.TopicSections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.OwnerId == ownerId && section.DocumentId == documentId)
            .ToListAsync(cancellationToken);
        db.TopicSections.RemoveRange(sections);

        // The topics in question, and every subtopic of them, since a subtopic that stays keeps its parent.
        var touched = sections.Select(section => section.TopicId).ToHashSet();
        var candidates = await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && (topic.DocumentId == documentId || touched.Contains(topic.Id)))
            .ToListAsync(cancellationToken);
        var candidateIds = candidates.Select(topic => topic.Id).ToList();
        var children = await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && topic.ParentId != null && candidateIds.Contains(topic.ParentId.Value))
            .ToListAsync(cancellationToken);
        var topics = candidates.Concat(children).DistinctBy(topic => topic.Id).ToList();
        var ids = topics.Select(topic => topic.Id).ToList();
        var withOtherSections = (await db.TopicSections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(section => section.OwnerId == ownerId && section.DocumentId != documentId && ids.Contains(section.TopicId))
                .Select(section => section.TopicId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var removed = DocumentRemoval.TopicsToRemove(documentId, topics, touched, withOtherSections);
        var gone = topics.Where(topic => removed.Contains(topic.Id)).ToList();
        db.Topics.RemoveRange(gone);
        return gone;
    }
}