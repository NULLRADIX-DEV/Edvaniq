using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Documents(
    ContentDbContext db, ICurrentUser currentUser, ITestDoubleCaller caller, IEventOutbox outbox, IFiles files, TimeProvider time) : IDocuments
{
    public async Task<Document> PasteAsync(LearningGoalInfo learningGoal, string? title, string text, CancellationToken cancellationToken)
    {
        var document = Document.Paste(
            currentUser.Id,
            learningGoal.Id,
            title,
            text,
            time.GetUtcNow().UtcDateTime,
            caller.UsesTestDouble);
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        return document;
    }

    public async Task<Document> UploadAsync(
        LearningGoalInfo learningGoal, string? fileName, Guid fileId, FileFormat format, CancellationToken cancellationToken)
    {
        var document = Document.Upload(
            currentUser.Id,
            learningGoal.Id,
            fileName,
            fileId,
            format,
            time.GetUtcNow().UtcDateTime,
            caller.UsesTestDouble);
        db.Documents.Add(document);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The caller deletes the file next, through the same context. A document still in it would go along.
            db.Entry(document).State = EntityState.Detached;
            throw;
        }

        return document;
    }

    public Task<int> CountInGoalAsync(Guid learningGoalId, CancellationToken cancellationToken) =>
        db.Documents.CountAsync(document => document.LearningGoalId == learningGoalId, cancellationToken);

    // Leaves the text in the database, ten documents could be two megabytes of it.
    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
        await db.Documents.OrderByDescending(document => document.CreatedAt)
            .Select(document => new DocumentSummary(
                document.Id, document.Title, document.Status, document.FailureReason, document.FailureCode, document.CreatedAt, document.FileId != null))
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

    // A worker may save the document between our read and our save, then the status no longer matches and we read it
    // again. Its own save fails once the row is gone, so nothing of the document goes out after DocumentDeleted. The
    // uploaded file goes first, bytes and row, as with UserDeleted: if the rest fails, a second try finds only the
    // document left.
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var document = await db.Documents.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (document is null)
            {
                return false;
            }

            if (document.FileId is { } fileId)
            {
                await files.DeleteAsync(fileId, cancellationToken);
            }

            db.Documents.Remove(document);
            outbox.Add(new DocumentDeleted(document.OwnerId, document.Id, document.LearningGoalId));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxDeleteAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private const int MaxDeleteAttempts = 3;
}