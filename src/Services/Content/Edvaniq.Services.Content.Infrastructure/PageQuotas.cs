using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;
using Edvaniq.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure;

// Runs in the Api under the user and in the worker without one, so the queries name the owner themselves.
internal sealed class PageQuotas(ContentDbContext db, TimeProvider time, IOptions<PageQuotaOptions> options) : IPageQuota
{
    public async Task<PageQuotaState> StateOfAsync(string ownerId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var today = StudyDay.Of(now);
        var used = await db.PageCharges.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(charge => charge.OwnerId == ownerId && charge.StudyDay == today)
            .SumAsync(charge => charge.Pages, cancellationToken);
        return new PageQuotaState(options.Value.PagesPerDay, used, StudyDay.NextStartUtc(now));
    }

    public Task<bool> IsChargedAsync(Guid documentId, CancellationToken cancellationToken) =>
        db.PageCharges.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AnyAsync(charge => charge.DocumentId == documentId, cancellationToken);

    public void Charge(Document document, int pages) =>
        db.PageCharges.Add(PageCharge.For(document, pages, time.GetUtcNow().UtcDateTime));
}