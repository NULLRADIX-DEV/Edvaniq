using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

public enum ConfirmResult
{
    Confirmed,
    Invalid,
    Expired,
    Used,
}

// Confirming the address of a new account (#250). Registration asks for the first link, a user who lost it asks for a
// new one, at most once a minute and five times a day per account, and a new one replaces the older ones. Asking never
// tells whether the address has an account. The operator confirms by hand while there is no mail service
// (account confirm).
public sealed class Confirmation(
    IAccounts accounts, IAccountTokens tokens, IMailOutbox outbox, SystemMails mails, IIdentityTransactions transactions, TimeProvider time)
{
    public static readonly TimeSpan ResendPause = TimeSpan.FromMinutes(1);

    public const int ResendsPerDay = 5;

    // Gives a new account its first link, saved with the account. The link and its token come when the mail is sent.
    public void Send(Account account) => mails.Confirmation(account);

    // A link holds once: of two uses at once, exactly one takes the token (TryUseAsync), the other finds it used.
    public async Task<ConfirmResult> ConfirmAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ConfirmResult.Invalid;
        }

        var hash = AccountToken.HashOf(value.Trim());
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await transactions.BeginAsync(cancellationToken);
        if (await tokens.TryUseAsync(hash, TokenPurpose.Confirmation, now, cancellationToken) is not { } accountId)
        {
            return await tokens.FindByHashAsync(hash, cancellationToken) switch
            {
                { Purpose: TokenPurpose.Confirmation, UsedAt: not null } => ConfirmResult.Used,
                { Purpose: TokenPurpose.Confirmation } => ConfirmResult.Expired,
                _ => ConfirmResult.Invalid,
            };
        }

        var confirmed = await accounts.FindByIdAsync(accountId, cancellationToken) is { } account && account.Confirm(now);
        await accounts.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return confirmed ? ConfirmResult.Confirmed : ConfirmResult.Used;
    }

    // Always the same answer: whether the address has an account, is confirmed already or asked too often stays unsaid.
    // The account's row is locked while its limits are checked, so asking many times at once counts like one after the
    // other. A mail still waiting to go out is not doubled. Returns whether mails come late.
    public async Task<bool> ResendAsync(string? email, CancellationToken cancellationToken)
    {
        if (AccountEmail.Normalize(email) is { } normalized)
        {
            await using var transaction = await transactions.BeginAsync(cancellationToken);
            await accounts.LockByEmailAsync(normalized, cancellationToken);
            var now = time.GetUtcNow().UtcDateTime;
            if (await accounts.FindByEmailAsync(normalized, cancellationToken) is { Status: AccountStatus.Unconfirmed } account
                && !await outbox.AnyWaitingForAsync(account.Id, MailKind.Confirmation, cancellationToken))
            {
                var sent = await tokens.SinceAsync(account.Id, TokenPurpose.Confirmation, now - TimeSpan.FromDays(1), cancellationToken);
                if (sent.Count < ResendsPerDay && (sent.Count == 0 || now - sent[0].CreatedAt >= ResendPause))
                {
                    Send(account);
                    await accounts.SaveAsync(cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return await mails.DelayedAsync(cancellationToken);
    }

    // The operator's confirmation (account confirm <email>), with the same age rule. False if there is no such
    // unconfirmed account.
    public async Task<bool> ConfirmByOperatorAsync(string email, CancellationToken cancellationToken)
    {
        if (AccountEmail.Normalize(email) is not { } normalized
            || await accounts.FindByEmailAsync(normalized, cancellationToken) is not { } account
            || !account.Confirm(time.GetUtcNow().UtcDateTime))
        {
            return false;
        }

        await accounts.SaveAsync(cancellationToken);
        return true;
    }
}