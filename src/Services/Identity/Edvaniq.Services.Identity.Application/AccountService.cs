using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

// Login and the command that sets up a team account. Neither ever logs or returns the password or its hash.
public sealed class AccountService(IAccounts accounts, TimeProvider time)
{
    // The id of the account that may log in, or null. A wrong login and a wrong password cost one hash each, so the time
    // tells them apart no more than the answer does.
    public async Task<Guid?> LoginAsync(string? login, string password, CancellationToken cancellationToken) =>
        await AuthenticateAsync(login, password, cancellationToken) is { Refusal: null, Account: { } account } ? account.Id : null;

    // A team account by its name, a user account by its address (an @ tells them apart). Why an account can't log in
    // yet is told only with the right password: a wrong one says nothing about the account, not even that it exists.
    public async Task<LoginResult> AuthenticateAsync(string? login, string password, CancellationToken cancellationToken)
    {
        Account? account = null;
        if (AccountEmail.IsEmail(login))
        {
            if (AccountEmail.Normalize(login) is { } email)
            {
                account = await accounts.FindByEmailAsync(email, cancellationToken);
            }
        }
        else if (AccountName.Normalize(login) is { } name)
        {
            account = await accounts.FindAsync(name, cancellationToken);
        }

        if (!PasswordHasher.Verify(password, account?.PasswordHash) || account is null)
        {
            return new LoginResult(null, LoginRefusal.InvalidCredentials);
        }

        return account.Status switch
        {
            AccountStatus.Unconfirmed => new LoginResult(account, LoginRefusal.Unconfirmed),
            AccountStatus.AwaitingApproval => new LoginResult(account, LoginRefusal.AwaitingApproval),
            _ => new LoginResult(account, null),
        };
    }

    // Creates the account or gives an existing one a new password, and marks it for tests or not. Throws
    // ArgumentException for an invalid name or password, the message names the rule, never the value.
    public async Task<(Account Account, bool Created)> SetPasswordAsync(
        string name, string password, CancellationToken cancellationToken, bool usesTestDouble = false)
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

        account.SetTestDouble(usesTestDouble);

        await accounts.SaveAsync(cancellationToken);
        return (account, created);
    }
}

// Refusal is null when the account may log in.
public sealed record LoginResult(Account? Account, LoginRefusal? Refusal);

public enum LoginRefusal
{
    // Unknown login or wrong password, never told apart.
    InvalidCredentials,

    Unconfirmed,

    AwaitingApproval,
}