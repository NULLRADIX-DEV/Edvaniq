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

    public Task<bool> AnyWaitingSinceAsync(DateTime before, CancellationToken cancellationToken) =>
        db.OutgoingMails.AnyAsync(mail => mail.CreatedAt <= before, cancellationToken);
}

// Sends the mails of the outbox (#248). A mail that fails waits and is tried again (OutgoingMail.RetryDelays), a sent one
// goes, and so does one older than OutgoingMail.MaxAge, sent or not: its link has run out by then. Without a mail service
// (MailProvider.None) mails only wait. The log names a mail by id and kind, never address, subject or text.
public sealed partial class MailDispatcher(
    IServiceScopeFactory scopes, IOptions<MailOptions> options, TimeProvider time, ILogger<MailDispatcher> logger,
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

    // One round: drops what is too old, then sends what is due. Returns how many were sent.
    public async Task<int> SendDueAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        var expired = await db.OutgoingMails.Where(mail => mail.CreatedAt <= now - OutgoingMail.MaxAge).ExecuteDeleteAsync(cancellationToken);
        if (expired > 0)
        {
            LogExpired(logger, expired, OutgoingMail.MaxAge);
        }

        if (sender is null)
        {
            var waiting = await db.OutgoingMails.CountAsync(cancellationToken);
            if (waiting > 0 && (reportedWaitingAt is null || now - reportedWaitingAt >= WaitingReport))
            {
                LogNoMailService(logger, waiting);
                reportedWaitingAt = now;
            }

            return 0;
        }

        var due = await db.OutgoingMails.Where(mail => mail.DueAt <= now).OrderBy(mail => mail.DueAt).Take(Batch).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var mail in due)
        {
            try
            {
                await sender.SendAsync(new MailMessage(mail.To, mail.Subject, mail.Body), cancellationToken);
                db.OutgoingMails.Remove(mail);
                sent++;
                LogSent(logger, mail.Id, mail.Kind, mail.Attempts + 1);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                mail.Postpone(time.GetUtcNow().UtcDateTime);
                LogFailed(logger, mail.Id, mail.Kind, mail.Attempts, exception.GetType().Name, mail.DueAt);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
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