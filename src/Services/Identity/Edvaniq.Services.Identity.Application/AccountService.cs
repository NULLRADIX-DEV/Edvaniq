using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

// Login and the command that sets up a team account. Neither ever logs or returns the password or its hash.
public sealed class AccountService(IAccounts accounts, TimeProvider time)
{
    // The id of the account, or null for a wrong name or password. Both cost one hash, so the time tells them apart no
    // more than the answer does.
    public async Task<Guid?> LoginAsync(string? name, string password, CancellationToken cancellationToken)
    {
        var normalized = AccountName.Normalize(name);
        var account = normalized is null ? null : await accounts.FindAsync(normalized, cancellationToken);
        return PasswordHasher.Verify(password, account?.PasswordHash) ? account!.Id : null;
    }

    // Creates the account or gives an existing one a new password. Throws ArgumentException for an invalid name or
    // password, the message names the rule, never the value.
    public async Task<(Account Account, bool Created)> SetPasswordAsync(string name, string password, CancellationToken cancellationToken)
    {
        var normalized = AccountName.Normalize(name)
            ?? throw new ArgumentException($"The name may only have a-z, 0-9, dot, dash and underscore, at most {Account.NameMaxLength} characters.");
        if (PasswordHasher.Check(password) is { } problem)
        {
            throw new ArgumentException(problem);
        }

        var hash = PasswordHasher.Hash(password);
        var account = await accounts.FindAsync(normalized, cancellationToken);
        var created = account is null;
        if (account is null)
        {
            account = Account.Create(normalized, hash, time.GetUtcNow().UtcDateTime);
            accounts.Add(account);
        }
        else
        {
            account.SetPasswordHash(hash);
        }

        await accounts.SaveAsync(cancellationToken);
        return (account, created);
    }
}