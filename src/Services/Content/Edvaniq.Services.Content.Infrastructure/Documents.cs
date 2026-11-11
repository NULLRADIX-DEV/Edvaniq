using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Documents(ContentDbContext db, ICurrentUser currentUser, TimeProvider time) : IDocuments
{
    public async Task<Document> PasteAsync(DefaultLearningGoal learningGoal, string? title, string text, CancellationToken cancellationToken)
    {
        var document = Document.Paste(
            currentUser.Id, learningGoal.Id, title, text, time.GetUtcNow().UtcDateTime, learningGoal.Level, learningGoal.OutputLanguage);
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        return document;
    }

    // Leaves the text in the database, ten documents could be two megabytes of it.
    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
        await db.Documents.OrderByDescending(document => document.CreatedAt)
            .Select(document => new DocumentSummary(document.Id, document.Title, document.Status, document.FailureReason, document.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Documents.AsNoTracking().FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    // The status is a concurrency token: of two retries at once only the first save wins, the second finds the
    // document no longer failed.
    public async Task<(RetryResult Result, Document? Document)> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (document is null)
        {
            return (RetryResult.NotFound, null);
        }

        if (document.Status != DocumentStatus.Failed)
        {
            return (RetryResult.NotFailed, document);
        }

        document.Retry(time.GetUtcNow().UtcDateTime);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return (RetryResult.NotFailed, await FindAsync(id, cancellationToken));
        }

        return (RetryResult.Started, document);
    }
}