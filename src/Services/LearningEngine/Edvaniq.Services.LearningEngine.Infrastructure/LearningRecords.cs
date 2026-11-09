using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.LearningEngine.Infrastructure;

// Runs in a consumer without a current user, so the owner filter would throw. The answerId is unique across users.
internal sealed class LearningRecords(LearningEngineDbContext db) : ILearningRecords
{
    public Task<bool> ExistsAsync(Guid answerId, CancellationToken cancellationToken) =>
        db.LearningRecords.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .AnyAsync(record => record.AnswerId == answerId, cancellationToken);

    public void Add(LearningRecord record) => db.LearningRecords.Add(record);
}