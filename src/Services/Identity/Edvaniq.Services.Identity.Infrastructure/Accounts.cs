using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

using Microsoft.EntityFrameworkCore;

using MySql.Data.MySqlClient;

namespace Edvaniq.Services.Identity.Infrastructure;

internal sealed class Accounts(IdentityDbContext db) : IAccounts
{
    private const int DuplicateKey = 1062;

    public Task<Account?> FindAsync(string name, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(account => account.Name == name, cancellationToken);

    public Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    public Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Accounts.FirstOrDefaultAsync(account => account.Email == email, cancellationToken);

    public void Add(Account account) => db.Accounts.Add(account);

    public void Remove(Account account) => db.Accounts.Remove(account);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<Account>> AwaitingApprovalAsync(CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking()
            .Where(account => account.Status == AccountStatus.AwaitingApproval)
            .OrderBy(account => account.ConfirmedAt)
            .ToListAsync(cancellationToken);

    // A locking read: a second transaction that wants the row waits until this one ends.
    public Task LockByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM Accounts WHERE Email = {email} FOR UPDATE", cancellationToken);

    // The unique indexes on name and address decide which of two at once comes first.
    public async Task<bool> SaveUnlessTakenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is MySqlException { Number: DuplicateKey })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}

// A transaction on Identity's database. Every save in it joins it (ServiceDbContext saves in a transaction of its own
// only when none is open).
internal sealed class IdentityTransactions(IdentityDbContext db) : IIdentityTransactions
{
    public async Task<IIdentityTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new Transaction(await db.Database.BeginTransactionAsync(cancellationToken));

    private sealed class Transaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction inner) : IIdentityTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}