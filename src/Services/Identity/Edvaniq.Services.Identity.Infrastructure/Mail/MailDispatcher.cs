using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.Infrastructure.Mail;

internal sealed class MailOutbox(IdentityDbContext db) : IMailOutbox
{
    public void Add(OutgoingMail mail) => db.OutgoingMails.Add(mail);

    public Task<bool> AnyWaitingForAsync(Guid accountId, MailKind kind, CancellationToken cancellationToken) =>
        db.OutgoingMails.AnyAsync(mail => mail.AccountId == accountId && mail.Kind == kind, cancellationToken);

    public Task<bool> AnyWaitingSinceAsync(DateTime before, CancellationToken cancellationToken) =>
        db.OutgoingMails.AnyAsync(mail => mail.CreatedAt <= before, cancellationToken);
}

// Sends the mails of the outbox (#248). Each mail is written only now, and a link gets its token only now: its hash is
// saved together with the removal of the sent mail, a new confirmation link replaces the older ones. A mail that fails
// waits and is tried again (OutgoingMail.RetryDelays), no token of it is kept. One older than OutgoingMail.MaxAge goes,
// sent or not. Without a mail service (MailProvider.None) mails only wait, and no token is made at all. The log names a
// mail by id and kind, never address, text or token.
public sealed partial class MailDispatcher(
    IServiceScopeFactory scopes, IOptions<MailOptions> options, MailLinks links, TimeProvider time, ILogger<MailDispatcher> logger,
    IMailSender? sender = null) : BackgroundService
{
    public const int Batch = 20;

    private static readonly TimeSpan WaitingReport = TimeSpan.FromHours(1);

    private DateTime? reportedWaitingAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Dispatch)
        {
            return;
        }

        try
        {
            while (true)
            {
                try
                {
                    await SendDueAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogRoundFailed(logger, exception.GetType().Name, options.Value.Interval);
                }

                await Task.Delay(options.Value.Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // One round: drops what is too old, then sends what is due. Returns how many were sent. The database calls are
    // synchronous on purpose, MySql.Data's OpenAsync can hang forever (like the OutboxDispatcher).
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        var expired = db.OutgoingMails.Where(mail => mail.CreatedAt <= now - OutgoingMail.MaxAge).ExecuteDelete();
        if (expired > 0)
        {
            LogExpired(logger, expired, OutgoingMail.MaxAge);
        }

        if (sender is null)
        {
            var waiting = db.OutgoingMails.Count();
            if (waiting > 0 && (reportedWaitingAt is null || now - reportedWaitingAt >= WaitingReport))
            {
                LogNoMailService(logger, waiting);
                reportedWaitingAt = now;
            }

            return 0;
        }

        var due = db.OutgoingMails.Where(mail => mail.DueAt <= now).OrderBy(mail => mail.DueAt).Take(Batch).ToList();
        var sent = 0;
        foreach (var mail in due)
        {
            if (await SendAsync(db, mail, cancellationToken))
            {
                sent++;
            }

            db.SaveChanges();
        }

        return sent;
    }

    private async Task<bool> SendAsync(IdentityDbContext db, OutgoingMail mail, CancellationToken cancellationToken)
    {
        var account = db.Accounts.Single(item => item.Id == mail.AccountId);
        var now = time.GetUtcNow().UtcDateTime;
        AccountToken? token = null;
        string? value = null;
        if (SystemMails.TokenOf(mail.Kind) is { } purpose)
        {
            (token, value) = AccountToken.Create(account, purpose, now);
        }

        var content = SystemMails.Write(mail.Kind, links, value);
        try
        {
            await sender!.SendAsync(new MailMessage(account.Email!, content.Subject, content.Body), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            mail.Postpone(now);
            LogFailed(logger, mail.Id, mail.Kind, mail.Attempts, exception.GetType().Name, mail.DueAt);
            return false;
        }

        if (token is not null)
        {
            // The newest link holds, every older one of the same purpose is replaced.
            foreach (var older in db.AccountTokens.Where(item => item.AccountId == account.Id && item.Purpose == token.Purpose && item.UsedAt == null))
            {
                older.Use(now);
            }

            db.AccountTokens.Add(token);
        }

        db.OutgoingMails.Remove(mail);
        LogSent(logger, mail.Id, mail.Kind, mail.Attempts + 1);
        return true;
    }

    [LoggerMessage(EventId = 34100, Level = LogLevel.Information, Message = "Mail {MailId} ({Kind}) is sent after {Attempts} attempts")]
    private static partial void LogSent(ILogger logger, Guid mailId, MailKind kind, int attempts);

    // The type only: a message could quote the address.
    [LoggerMessage(EventId = 34101, Level = LogLevel.Warning, Message = "Mail {MailId} ({Kind}) failed {Attempts} times ({Error}), next attempt at {DueAt:o}")]
    private static partial void LogFailed(ILogger logger, Guid mailId, MailKind kind, int attempts, string error, DateTime dueAt);

    [LoggerMessage(EventId = 34102, Level = LogLevel.Warning, Message = "{Count} mails were older than {MaxAge} and are dropped unsent")]
    private static partial void LogExpired(ILogger logger, int count, TimeSpan maxAge);

    [LoggerMessage(EventId = 34103, Level = LogLevel.Warning, Message = "No mail service is set up, {Count} mails wait")]
    private static partial void LogNoMailService(ILogger logger, int count);

    [LoggerMessage(EventId = 34104, Level = LogLevel.Error, Message = "A round of mails failed ({Error}), the next starts in {Interval}")]
    private static partial void LogRoundFailed(ILogger logger, string error, TimeSpan interval);
}