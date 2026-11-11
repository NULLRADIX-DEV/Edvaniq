using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

// Deletes a team account and tells every service with user data in the same save (UserDeleted, docs/spec/events.md).
// The user id in the event is the sub of the account's tokens. Deleting by the user comes with M5.
public sealed class AccountDeletion(IAccounts accounts, IEventOutbox outbox)
{
    // False if there is no account with this name, then nothing goes out.
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        if (AccountName.Normalize(name) is not { } normalized
            || await accounts.FindAsync(normalized, cancellationToken) is not { } account)
        {
            return false;
        }

        accounts.Remove(account);
        outbox.Add(new UserDeleted(account.Id.ToString("D")));
        await accounts.SaveAsync(cancellationToken);
        return true;
    }
}