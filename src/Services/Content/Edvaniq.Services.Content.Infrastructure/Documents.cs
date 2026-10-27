using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Documents(ContentDbContext db, ICurrentUser currentUser, TimeProvider time) : IDocuments
{
    public async Task<Document> PasteAsync(string? title, string text, CancellationToken cancellationToken)
    {
        var document = Document.Paste(currentUser.Id, title, text, time.GetUtcNow().UtcDateTime);
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        return document;
    }

    // Leaves the text in the database, ten documents could be two megabytes of it.
    public async Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
        await db.Documents.OrderByDescending(document => document.CreatedAt)
            .Select(document => new DocumentSummary(document.Id, document.Title, document.Status, document.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Documents.AsNoTracking().FirstOrDefaultAsync(document => document.Id == id, cancellationToken);
}
