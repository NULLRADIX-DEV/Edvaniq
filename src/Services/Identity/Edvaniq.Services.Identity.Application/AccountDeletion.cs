using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

// Deletes an account and tells every service with user data in the same save (UserDeleted, docs/spec/events.md). The
// login tells the kind apart like at the login: an address (with "@") for a user account, else a team account's name.
// Its mails and links go with it. The user id in the event is the sub of the account's tokens. Deleting by the user
// comes with M5.
public sealed class AccountDeletion(IAccounts accounts, IEventOutbox outbox)
{
    // False if there is no such account, then nothing goes out.
    public async Task<bool> DeleteAsync(string login, CancellationToken cancellationToken)
    {
        var account = AccountEmail.IsEmail(login)
            ? AccountEmail.Normalize(login) is { } email ? await accounts.FindByEmailAsync(email, cancellationToken) : null
            : AccountName.Normalize(login) is { } name ? await accounts.FindAsync(name, cancellationToken) : null;
        if (account is null)
        {
            return false;
        }

        accounts.Remove(account);
        outbox.Add(new UserDeleted(account.Id.ToString("D")));
        await accounts.SaveAsync(cancellationToken);
        return true;
    }
}