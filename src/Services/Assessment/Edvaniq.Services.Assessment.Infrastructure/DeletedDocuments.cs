using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Assessment.Infrastructure;

// Runs in consumers without a current user, so the queries drop the owner filter and name the owner.
internal sealed class DeletedDocuments(AssessmentDbContext db) : IDeletedDocuments
{
    public Task<bool> IsDeletedAsync(string ownerId, Guid documentId, CancellationToken cancellationToken) =>
        db.DeletedDocuments.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .AnyAsync(document => document.OwnerId == ownerId && document.DocumentId == documentId, cancellationToken);

    public async Task<bool> RemoveAsync(string ownerId, Guid documentId, DateTime deletedAt, CancellationToken cancellationToken)
    {
        if (await IsDeletedAsync(ownerId, documentId, cancellationToken))
        {
            return false;
        }

        db.DeletedDocuments.Add(DeletedDocument.Of(ownerId, documentId, deletedAt));
        var questions = await db.Questions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(question => question.OwnerId == ownerId && question.DocumentId == documentId)
            .ToListAsync(cancellationToken);
        var ids = questions.Select(question => question.Id).ToList();
        db.Attempts.RemoveRange(await db.Attempts.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(attempt => attempt.OwnerId == ownerId && ids.Contains(attempt.QuestionId))
            .ToListAsync(cancellationToken));
        db.Questions.RemoveRange(questions);
        return true;
    }
}