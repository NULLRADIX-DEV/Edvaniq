using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

// The operator's approval of minors (#256, #268): who waits for it, letting an account in and taking that back. Only the
// date of an approval is kept, nothing of the consent behind it.
public sealed class Approvals(IAccounts accounts, TimeProvider time)
{
    // The accounts that wait, oldest confirmation first, with address and the time of confirmation.
    public Task<IReadOnlyList<Account>> PendingAsync(CancellationToken cancellationToken) => accounts.AwaitingApprovalAsync(cancellationToken);

    // The approved account, or null if there is none with this address that waits.
    public async Task<Account?> ApproveAsync(string email, CancellationToken cancellationToken)
    {
        if (await FindAsync(email, cancellationToken) is not { } account || !account.Approve(time.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        await accounts.SaveAsync(cancellationToken);
        return account;
    }

    // The account whose approval was taken back, or null if there is no approved one with this address. Tokens issued
    // before stay valid until they run out (docs/tokens.md).
    public async Task<Account?> RevokeAsync(string email, CancellationToken cancellationToken)
    {
        if (await FindAsync(email, cancellationToken) is not { } account || !account.Revoke())
        {
            return null;
        }

        await accounts.SaveAsync(cancellationToken);
        return account;
    }

    private async Task<Account?> FindAsync(string email, CancellationToken cancellationToken) =>
        AccountEmail.Normalize(email) is { } normalized ? await accounts.FindByEmailAsync(normalized, cancellationToken) : null;
}