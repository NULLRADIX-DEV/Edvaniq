using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

public interface IAccounts
{
    // By the normalized name.
    Task<Account?> FindAsync(string name, CancellationToken cancellationToken);

    Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    // By the normalized address.
    Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    void Add(Account account);

    void Remove(Account account);

    Task SaveAsync(CancellationToken cancellationToken);

    // Locks the row of the account with this normalized address until the open transaction ends, if there is one.
    Task LockByEmailAsync(string email, CancellationToken cancellationToken);

    // Saves, or returns false and keeps nothing if an account with the same name or address came first.
    Task<bool> SaveUnlessTakenAsync(CancellationToken cancellationToken);
}