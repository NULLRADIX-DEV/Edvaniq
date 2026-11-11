using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// Runs without a current user, in a consumer or for Content's worker, so the owner filter would throw: the queries
// drop it and name the owner or the document.
internal sealed class AnalyzedTopics(KnowledgeDbContext db) : IAnalyzedTopics
{
    public async Task ImportAsync(TopicImport import, CancellationToken cancellationToken)
    {
        db.Topics.AddRange(import.Topics);

        // Only the difference, so an assignment that stays is not deleted and added again under the same key.
        var stored = await db.TopicSections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(section => section.OwnerId == import.OwnerId && section.DocumentId == import.DocumentId)
            .ToListAsync(cancellationToken);
        var wanted = import.Sections.Select(section => (section.TopicId, section.SectionId)).ToHashSet();
        var kept = stored.Select(section => (section.TopicId, section.SectionId)).ToHashSet();
        db.TopicSections.RemoveRange(stored.Where(section => !wanted.Contains((section.TopicId, section.SectionId))));
        db.TopicSections.AddRange(import.Sections.Where(section => !kept.Contains((section.TopicId, section.SectionId))));
    }

    public async Task<IReadOnlyList<Topic>> OfGoalAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
        await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var sections = (await db.TopicSections.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .Where(section => section.DocumentId == documentId)
                .Select(section => new { section.TopicId, section.SectionId })
                .ToListAsync(cancellationToken))
            .ToLookup(section => section.TopicId, section => section.SectionId);
        var withSections = sections.Select(group => group.Key).ToList();

        // A topic the document made may have no sections of its own, only its subtopics have some.
        var topics = await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.DocumentId == documentId || withSections.Contains(topic.Id))
            .OrderBy(topic => topic.CreatedAt)
            .ThenBy(topic => topic.Position)
            .Select(topic => new { topic.Id, topic.ParentId, topic.Position })
            .ToListAsync(cancellationToken);

        return [.. topics.Select(topic => new DocumentTopic(topic.Id, topic.ParentId, topic.Position, [.. sections[topic.Id]]))];
    }
}