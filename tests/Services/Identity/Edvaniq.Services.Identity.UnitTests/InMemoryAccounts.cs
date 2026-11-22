using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.UnitTests;

internal sealed class InMemoryAccounts : IAccounts
{
    public List<Account> All { get; } = [];

    public Task<Account?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(All.FirstOrDefault(account => account.Name == name));

    public Task<Account?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(All.FirstOrDefault(account => account.Id == id));

    public Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(All.FirstOrDefault(account => account.Email == email));

    public void Add(Account account) => All.Add(account);

    public void Remove(Account account) => All.Remove(account);

    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> SaveUnlessTakenAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task LockByEmailAsync(string email, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<Account>> AwaitingApprovalAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Account>>([.. All.Where(account => account.Status == AccountStatus.AwaitingApproval)]);
}
