using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Identity.Infrastructure;

internal sealed class AccountTokens(IdentityDbContext db) : IAccountTokens
{
    public void Add(AccountToken token) => db.AccountTokens.Add(token);

    public Task<AccountToken?> FindByHashAsync(string hash, CancellationToken cancellationToken) =>
        db.AccountTokens.FirstOrDefaultAsync(token => token.Hash == hash, cancellationToken);

    public async Task<IReadOnlyList<AccountToken>> SinceAsync(
        Guid accountId, TokenPurpose purpose, DateTime since, CancellationToken cancellationToken) =>
        await db.AccountTokens
            .Where(token => token.AccountId == accountId && token.Purpose == purpose && token.CreatedAt >= since)
            .OrderByDescending(token => token.CreatedAt)
            .ToListAsync(cancellationToken);
}