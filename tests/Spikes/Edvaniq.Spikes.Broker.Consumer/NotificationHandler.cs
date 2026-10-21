namespace Edvaniq.Spikes.Broker.Consumer;

// Sync on purpose, MySql.Data's OpenAsync can hang forever (see MySqlHealthCheck).
public sealed class NotificationHandler(Func<InboxDbContext> createContext)
{
    // False if the message was handled before. Inbox entry and effect land in one SaveChanges.
    public bool Handle(Guid messageId, DocumentCreated documentCreated)
    {
        using var context = createContext();
        if (context.Inbox.Any(m => m.MessageId == messageId))
        {
            return false;
        }

        context.Inbox.Add(new InboxMessage { MessageId = messageId, ReceivedAt = DateTime.UtcNow });
        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            DocumentId = documentCreated.DocumentId,
            Text = $"Neues Dokument: {documentCreated.Title}",
        });
        context.SaveChanges();
        return true;
    }
}