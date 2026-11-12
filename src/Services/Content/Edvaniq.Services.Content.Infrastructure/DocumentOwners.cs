using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

internal sealed class DocumentOwners(ContentDbContext db) : IDocumentOwners
{
    public Task<string?> OwnerOfAsync(Guid documentId, CancellationToken cancellationToken) =>
        db.Documents.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AsNoTracking()
            .Where(document => document.Id == documentId)
            .Select(document => document.OwnerId)
            .FirstOrDefaultAsync(cancellationToken);
}