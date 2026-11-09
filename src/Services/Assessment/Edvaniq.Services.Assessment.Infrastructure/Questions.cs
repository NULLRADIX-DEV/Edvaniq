using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Assessment.Infrastructure;

// Runs in a consumer without a current user, so the owner filter would throw. The itemId is unique across users.
internal sealed class Questions(AssessmentDbContext db) : IQuestions
{
    public async Task<IReadOnlySet<Guid>> KnownAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Questions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(question => ids.Contains(question.Id))
            .Select(question => question.Id)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public void Add(Question question) => db.Questions.Add(question);
}