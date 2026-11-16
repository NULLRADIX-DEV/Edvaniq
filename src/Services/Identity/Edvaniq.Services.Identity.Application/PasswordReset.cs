using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

public enum ResetResult
{
    Done,
    Invalid,
    Expired,
    Used,
    PasswordRules,
}

// A new password by a link in a mail (#253). Asking always gets the same answer: a mail goes only to a confirmed user
// account, within the limits per account (#267). The link holds an hour and once, the new password follows the rules of
// registration, and with it every other open reset link goes. Tokens issued before stay valid until they run out
// (docs/tokens.md).
public sealed class PasswordReset(
    IAccounts accounts, IAccountTokens tokens, SystemMails mails, IIdentityTransactions transactions, TimeProvider time)
{
    // Returns whether mails come late, the same for any address.
    public async Task<bool> RequestAsync(string? email, CancellationToken cancellationToken)
    {
        if (AccountEmail.Normalize(email) is { } normalized)
        {
            await using var transaction = await transactions.BeginAsync(cancellationToken);
            await accounts.LockByEmailAsync(normalized, cancellationToken);
            if (await accounts.FindByEmailAsync(normalized, cancellationToken) is { Status: not AccountStatus.Unconfirmed } account
                && await mails.TryAsync(account, MailKind.PasswordReset, cancellationToken))
            {
                await accounts.SaveAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return await mails.DelayedAsync(cancellationToken);
    }

    // The account with its new password, or why not. A password against the rules costs no link.
    public async Task<(ResetResult Result, Account? Account)> CompleteAsync(string? value, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (ResetResult.Invalid, null);
        }

        if (password is null || PasswordHasher.Check(password) is not null)
        {
            return (ResetResult.PasswordRules, null);
        }

        var hash = AccountToken.HashOf(value.Trim());
        var passwordHash = PasswordHasher.Hash(password);
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (await tokens.TryUseAsync(hash, TokenPurpose.PasswordReset, now, cancellationToken) is not { } accountId)
        {
            var result = await tokens.FindByHashAsync(hash, cancellationToken) switch
            {
                { Purpose: TokenPurpose.PasswordReset, UsedAt: not null } => ResetResult.Used,
                { Purpose: TokenPurpose.PasswordReset } => ResetResult.Expired,
                _ => ResetResult.Invalid,
            };
            return (result, null);
        }

        if (await accounts.FindByIdAsync(accountId, cancellationToken) is not { } account)
        {
            return (ResetResult.Invalid, null);
        }

        account.SetPasswordHash(passwordHash);
        foreach (var other in await tokens.SinceAsync(account.Id, TokenPurpose.PasswordReset, DateTime.MinValue, cancellationToken))
        {
            other.Use(now);
        }

        await accounts.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (ResetResult.Done, account);
    }
}