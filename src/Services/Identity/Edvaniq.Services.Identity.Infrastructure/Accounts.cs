using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Identity.Infrastructure;

internal sealed class Accounts(IdentityDbContext db) : IAccounts
{
    public Task<Account?> FindAsync(string name, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(account => account.Name == name, cancellationToken);

    public void Add(Account account) => db.Accounts.Add(account);

    public void Remove(Account account) => db.Accounts.Remove(account);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}