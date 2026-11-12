using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

public interface IAccounts
{
    // By the normalized name.
    Task<Account?> FindAsync(string name, CancellationToken cancellationToken);

    // By the normalized address.
    Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    void Add(Account account);

    void Remove(Account account);

    Task SaveAsync(CancellationToken cancellationToken);
}