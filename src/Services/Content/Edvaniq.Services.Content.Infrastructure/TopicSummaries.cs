using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them. Knowledge decides
// whether the topic is the user's, its sections only count where they are the user's sections here.
internal sealed class TopicSummaries(
    ContentDbContext db, ITopicMaterial material, ICurrentUser currentUser, TimeProvider time) : ITopicSummaries
{
    public async Task<SummaryView?> FindAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken) =>
        await LoadAsync(topicId, length, cancellationToken) is { } summary ? await ViewAsync(summary, cancellationToken) : null;

    public async Task<(SummaryRequestResult Result, SummaryView? Summary)> RequestAsync(
        Guid topicId, SummaryLength length, CancellationToken cancellationToken)
    {
        // Knowledge first, even for a stored summary: only it knows whether the topic is still the user's.
        if (await material.SectionsAsync(topicId, cancellationToken) is not { } named)
        {
            return (SummaryRequestResult.TopicNotFound, null);
        }

        if (await LoadAsync(topicId, length, cancellationToken) is { } stored)
        {
            if (!stored.Retry())
            {
                return (SummaryRequestResult.Existing, await ViewAsync(stored, cancellationToken));
            }

            await db.SaveChangesAsync(cancellationToken);
            return (SummaryRequestResult.Retried, await ViewAsync(stored, cancellationToken));
        }

        var ids = named.Select(item => item.SectionId).ToList();
        var own = await db.Sections.AsNoTracking()
            .Where(section => ids.Contains(section.Id))
            .Join(db.Documents.AsNoTracking(), section => section.DocumentId, document => document.Id,
                (section, document) => new { section.Id, section.DocumentId, section.Ordinal, document.CreatedAt, document.LearningGoalId })
            .ToListAsync(cancellationToken);
        if (own.Count == 0)
        {
            return (SummaryRequestResult.NoMaterial, null);
        }

        // The documents in the order they came, each in the order of its text.
        var ordered = own.OrderBy(item => item.CreatedAt).ThenBy(item => item.DocumentId).ThenBy(item => item.Ordinal).ToList();
        var summary = TopicSummary.Request(
            currentUser.Id, topicId, ordered[0].LearningGoalId, length, [.. ordered.Select(item => (item.DocumentId, item.Id))],
            time.GetUtcNow().UtcDateTime);
        db.TopicSummaries.Add(summary);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same request at the same moment: the other one's summary stands.
            db.ChangeTracker.Clear();
            return await LoadAsync(topicId, length, cancellationToken) is { } other
                ? (SummaryRequestResult.Existing, await ViewAsync(other, cancellationToken))
                : throw new InvalidOperationException("The summary was neither stored nor found.");
        }

        return (SummaryRequestResult.Created, await ViewAsync(summary, cancellationToken));
    }

    private Task<TopicSummary?> LoadAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken) =>
        db.TopicSummaries.Include(summary => summary.Sections)
            .FirstOrDefaultAsync(summary => summary.TopicId == topicId && summary.Length == length, cancellationToken);

    // The sources are the used sections, one entry per document from the first to the last page they stand on.
    private async Task<SummaryView> ViewAsync(TopicSummary summary, CancellationToken cancellationToken)
    {
        var used = summary.Sections.Where(section => section.Used).ToList();
        if (used.Count == 0)
        {
            return new SummaryView(summary, []);
        }

        var sectionIds = used.Select(section => section.SectionId).ToList();
        var documentIds = used.Select(section => section.DocumentId).Distinct().ToList();
        var sections = await db.Sections.AsNoTracking().Where(section => sectionIds.Contains(section.Id)).ToListAsync(cancellationToken);
        var pages = await db.DocumentPages.AsNoTracking().Where(page => documentIds.Contains(page.DocumentId)).ToListAsync(cancellationToken);
        var titles = await db.Documents.AsNoTracking().Where(document => documentIds.Contains(document.Id))
            .ToDictionaryAsync(document => document.Id, document => document.Title, cancellationToken);

        var sources = new List<SummarySource>();
        foreach (var documentId in used.Select(section => section.DocumentId).Distinct())
        {
            if (!titles.TryGetValue(documentId, out var title))
            {
                continue;
            }

            var ofDocument = pages.Where(page => page.DocumentId == documentId).ToList();
            var ranges = sections.Where(section => section.DocumentId == documentId).Select(section => section.PagesIn(ofDocument)).ToList();
            var from = ranges.Select(range => range.From).Where(page => page is not null).Min();
            var to = ranges.Select(range => range.To).Where(page => page is not null).Max();
            sources.Add(new SummarySource(documentId, title, from, to));
        }

        return new SummaryView(summary, sources);
    }
}