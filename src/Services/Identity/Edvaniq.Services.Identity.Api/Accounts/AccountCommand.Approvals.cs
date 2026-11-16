using System.Globalization;

using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Api.Accounts;

// account approve <email>, account revoke <email> and account pending (#268): the operator lets a minor in after the
// parents' written consent, takes it back, and lists who waits. Addresses go to the operator's console only, the log
// names an account by id.
internal static partial class AccountCommand
{
    private static async Task<int> ApprovalsAsync(IServiceProvider services, string[] args, TextWriter output, TextWriter error)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccountCommand));
        try
        {
            using var scope = services.CreateScope();
            var approvals = scope.ServiceProvider.GetRequiredService<Approvals>();
            switch (args)
            {
                case ["account", "approve", var email]:
                    var approved = await approvals.ApproveAsync(email, CancellationToken.None);
                    if (approved is not null)
                    {
                        LogApproved(logger, approved.Id);
                    }

                    await output.WriteLineAsync(approved is null ? "no account" : "approved");
                    return Ok;
                case ["account", "revoke", var email]:
                    var revoked = await approvals.RevokeAsync(email, CancellationToken.None);
                    if (revoked is not null)
                    {
                        LogRevoked(logger, revoked.Id);
                    }

                    await output.WriteLineAsync(revoked is null ? "no account" : "revoked");
                    return Ok;
                case ["account", "pending"]:
                    var pending = await approvals.PendingAsync(CancellationToken.None);
                    foreach (var account in pending)
                    {
                        await output.WriteLineAsync(string.Create(
                            CultureInfo.InvariantCulture, $"{account.Email} {account.BirthYear} {account.ConfirmedAt:yyyy-MM-dd}"));
                    }

                    LogPending(logger, pending.Count);
                    return Ok;
                default:
                    await error.WriteLineAsync("Usage: account approve <email>, account revoke <email> or account pending.");
                    return Invalid;
            }
        }
        catch (Exception exception)
        {
            LogApprovalFailed(logger, exception);
            await error.WriteLineAsync("Changing the approval failed, see the log above.");
            return Failed;
        }
    }

    // account consent <email> (#261): the proof of what the user agreed to, to the console only.
    private static async Task<int> ConsentAsync(IServiceProvider services, string email, TextWriter output, TextWriter error)
    {
        try
        {
            using var scope = services.CreateScope();
            var account = AccountEmail.Normalize(email) is { } normalized
                ? await scope.ServiceProvider.GetRequiredService<IAccounts>().FindByEmailAsync(normalized, CancellationToken.None)
                : null;
            await output.WriteLineAsync(account?.ConsentedAt is { } at
                ? string.Create(CultureInfo.InvariantCulture, $"terms {account.TermsVersion} privacy {account.PrivacyVersion} at {at:yyyy-MM-ddTHH:mm:ssZ}")
                : "no consent");
            return Ok;
        }
        catch (Exception exception)
        {
            LogApprovalFailed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccountCommand)), exception);
            await error.WriteLineAsync("Reading the consent failed, see the log above.");
            return Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The operator approved account {AccountId}")]
    private static partial void LogApproved(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The operator took the approval of account {AccountId} back")]
    private static partial void LogRevoked(ILogger logger, Guid accountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} accounts wait for approval")]
    private static partial void LogPending(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Changing the approval failed")]
    private static partial void LogApprovalFailed(ILogger logger, Exception exception);
}