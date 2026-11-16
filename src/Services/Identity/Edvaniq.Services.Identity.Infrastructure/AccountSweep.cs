using Edvaniq.Services.Identity.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Identity.Infrastructure;

// Clears up after registrations (#266): a user account whose address isn't confirmed within UnconfirmedFor goes, with
// its mails and links (cascade). It never logged in, so no other service holds anything of it, and no UserDeleted goes
// out. A link (confirmation or reset) that ran out more than ExpiredFor ago goes too, until then it still answers
// "expired". The log names counts only. The database calls are synchronous on purpose, MySql.Data's OpenAsync can hang
// forever.
public sealed partial class AccountSweep(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, ILogger<AccountSweep> logger) : BackgroundService
{
    public static readonly TimeSpan UnconfirmedFor = TimeSpan.FromDays(7);

    public static readonly TimeSpan ExpiredFor = TimeSpan.FromDays(7);

    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    // Tests switch the rounds in the background off and sweep by hand.
    public const string Switch = "Accounts:Sweep";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue(Switch, true))
        {
            return;
        }

        try
        {
            while (true)
            {
                try
                {
                    Sweep();
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogFailed(logger, exception.GetType().Name, Interval);
                }

                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // One round. Returns how many accounts and links went.
    public (int Accounts, int Links) Sweep()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        var accounts = db.Accounts
            .Where(account => account.Kind == AccountKind.User && account.Status == AccountStatus.Unconfirmed && account.CreatedAt <= now - UnconfirmedFor)
            .ExecuteDelete();
        if (accounts > 0)
        {
            LogAccounts(logger, accounts, UnconfirmedFor);
        }

        var links = db.AccountTokens
            .Where(token => token.ExpiresAt <= now - ExpiredFor)
            .ExecuteDelete();
        if (links > 0)
        {
            LogLinks(logger, links);
        }

        return (accounts, links);
    }

    [LoggerMessage(EventId = 34110, Level = LogLevel.Information, Message = "{Count} accounts not confirmed within {Age} are deleted")]
    private static partial void LogAccounts(ILogger logger, int count, TimeSpan age);

    [LoggerMessage(EventId = 34111, Level = LogLevel.Information, Message = "{Count} expired links are deleted")]
    private static partial void LogLinks(ILogger logger, int count);

    [LoggerMessage(EventId = 34112, Level = LogLevel.Error, Message = "Clearing up accounts failed ({Error}), the next round starts in {Interval}")]
    private static partial void LogFailed(ILogger logger, string error, TimeSpan interval);
}