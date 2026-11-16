using System.Collections.Concurrent;

namespace Edvaniq.Services.Identity.Infrastructure.Mail;

// Hands one mail to the mail service. Throws when it can't, the dispatcher tries again later.
public interface IMailSender
{
    Task SendAsync(MailMessage message, CancellationToken cancellationToken);
}

public sealed record MailMessage(string To, string Subject, string Body);

// The test double: sends nothing and keeps every mail it got, so tests read them. Away while Outage is set, like a mail
// service that doesn't answer.
public sealed class RecordingMailSender : IMailSender
{
    private readonly ConcurrentQueue<MailMessage> sent = new();

    public bool Outage { get; set; }

    public IReadOnlyList<MailMessage> Sent => [.. sent];

    public Task SendAsync(MailMessage message, CancellationToken cancellationToken)
    {
        if (Outage)
        {
            throw new IOException("The mail service is away.");
        }

        sent.Enqueue(message);
        return Task.CompletedTask;
    }
}