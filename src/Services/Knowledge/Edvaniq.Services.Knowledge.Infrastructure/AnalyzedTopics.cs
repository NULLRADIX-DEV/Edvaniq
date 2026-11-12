using System.Data;

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

        // The terms of the document come new with every analysis of it, the old ones go.
        db.KeyTerms.RemoveRange(await db.KeyTerms.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(term => term.OwnerId == import.OwnerId && term.DocumentId == import.DocumentId)
            .ToListAsync(cancellationToken));
        db.KeyTerms.AddRange(import.Terms ?? []);
    }

    public async Task<IReadOnlyList<Topic>> OfGoalAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken) =>
        await db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(topic => topic.OwnerId == ownerId && topic.LearningGoalId == learningGoalId)
            .ToListAsync(cancellationToken);

    // Both reads in one snapshot: the analysis saves topics and sections together, and a save between two loose reads
    // gave topics without their sections, so Content saw no material and made no questions (#985). Synchronous on purpose,
    // MySql.Data's async open can hang (MySqlHealthCheck).
    public async Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var snapshot = db.Database.BeginTransaction(IsolationLevel.RepeatableRead);
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