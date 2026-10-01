using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;
using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class ExampleItems(PlanningDbContext db, ICurrentUser currentUser) : IExampleItems
{
    public async Task<ExampleItem> AddAsync(string text, CancellationToken cancellationToken)
    {
        var item = new ExampleItem(currentUser.Id, text);
        db.ExampleItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return item;
    }

    public async Task<IReadOnlyList<ExampleItem>> ListAsync(CancellationToken cancellationToken) =>
        await db.ExampleItems.AsNoTracking().OrderBy(item => item.Text).ToListAsync(cancellationToken);

    public Task<ExampleItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.ExampleItems.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
}
