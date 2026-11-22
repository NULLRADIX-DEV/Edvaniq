using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

// What counts the wrong passwords of a login (#252). The change of a password asks it too, so a stolen session can't
// try passwords here instead of at the login.
public interface ILoginAttempts
{
    // True and the wait while the login is locked.
    bool IsLocked(string? login, out TimeSpan retryAfter);

    void Failed(string? login);
}

// A logged-in account sets a new password (#254). The account comes from the token, never from the request. Neither
// password nor hash is ever logged or returned.
public sealed class PasswordChange(IAccounts accounts, ILoginAttempts attempts)
{
    public const string CurrentInvalidCode = "current_password_invalid";

    public const string CurrentInvalidTitle = "Dein aktuelles Passwort stimmt nicht.";

    public const string CurrentMissingTitle = "Gib dein aktuelles Passwort ein.";

    // A wrong current password counts like a wrong password at the login, under the same key. A locked login says no
    // before the password is looked at, also to the right one. A success does not reset the count.
    public async Task<PasswordChangeResult> ChangeAsync(
        Guid accountId, string? currentPassword, string? newPassword, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            return new PasswordChangeResult(PasswordChangeOutcome.AccountNotFound);
        }

        // No password is no guess, so it doesn't count: a client sending the wrong field must not lock the login.
        if (string.IsNullOrEmpty(currentPassword))
        {
            return new PasswordChangeResult(PasswordChangeOutcome.CurrentInvalid, CurrentMissingTitle);
        }

        var login = account.Kind == AccountKind.User ? account.Email : account.Name;
        if (attempts.IsLocked(login, out var retryAfter))
        {
            return new PasswordChangeResult(PasswordChangeOutcome.Locked, RetryAfter: retryAfter);
        }

        // A password beyond the limit can't be the current one, no need to spend a hash on it.
        if (currentPassword.Length > PasswordHasher.MaximumLength
            || !PasswordHasher.Verify(currentPassword, account.PasswordHash))
        {
            attempts.Failed(login);
            return new PasswordChangeResult(PasswordChangeOutcome.CurrentInvalid, CurrentInvalidTitle);
        }

        if (PasswordPolicy.Violation(newPassword) is { } violation)
        {
            return new PasswordChangeResult(PasswordChangeOutcome.RulesViolated, violation);
        }

        account.SetPasswordHash(PasswordHasher.Hash(newPassword!));
        await accounts.SaveAsync(cancellationToken);
        return new PasswordChangeResult(PasswordChangeOutcome.Changed);
    }
}

// Detail is the sentence for the user, set for the two refusals of the request.
public sealed record PasswordChangeResult(PasswordChangeOutcome Outcome, string? Detail = null, TimeSpan RetryAfter = default);

public enum PasswordChangeOutcome
{
    Changed,

    AccountNotFound,

    // The login of the account is locked after too many wrong passwords.
    Locked,

    CurrentInvalid,

    RulesViolated,
}
