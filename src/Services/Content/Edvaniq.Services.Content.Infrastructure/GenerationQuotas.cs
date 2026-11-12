using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;
using Edvaniq.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure;

// Runs in the Api under the user and in the worker without one, so the query names the owner itself.
internal sealed class GenerationQuotas(ContentDbContext db, TimeProvider time, IOptions<GenerationQuotaOptions> options) : IGenerationQuota
{
    public async Task<GenerationQuotaState> StateOfAsync(string ownerId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var today = StudyDay.Of(now);
        var used = await db.GenerationCharges.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .CountAsync(charge => charge.OwnerId == ownerId && charge.StudyDay == today, cancellationToken);
        return new GenerationQuotaState(options.Value.GenerationsPerDay, used, StudyDay.NextStartUtc(now));
    }

    public void Charge(Document document, Guid topicId) =>
        db.GenerationCharges.Add(GenerationCharge.For(document, topicId, time.GetUtcNow().UtcDateTime));
}