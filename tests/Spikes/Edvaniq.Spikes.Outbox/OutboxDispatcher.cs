using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Spikes.Outbox;

// Sync on purpose, MySql.Data's OpenAsync can hang forever (see MySqlHealthCheck).
public sealed class OutboxDispatcher(Func<SpikeDbContext> createContext, ITransport transport)
{
    // Returns how many went out. If the commit fails after sending, the batch goes out again.
    public int DispatchOnce(int batchSize = 50)
    {
        using var context = createContext();
        using var transaction = context.Database.BeginTransaction();

        // SKIP LOCKED: a second dispatcher takes the next rows instead of waiting for ours.
        var batch = context.Outbox
            .FromSql($"SELECT * FROM outbox_messages WHERE SentAt IS NULL ORDER BY Id LIMIT {batchSize} FOR UPDATE SKIP LOCKED")
            .ToList();

        var sent = 0;
        foreach (var message in batch)
        {
            message.Attempts++;
            try
            {
                transport.Send(message);
                message.SentAt = DateTime.UtcNow;
                message.LastError = null;
                sent++;
            }
            catch (TransportException exception)
            {
                message.LastError = exception.Message;
            }
        }

        context.SaveChanges();
        transaction.Commit();
        return sent;
    }

    // Stops once a run sends nothing, so a message that keeps failing doesn't spin forever.
    public void DispatchAll(int batchSize = 50)
    {
        while (DispatchOnce(batchSize) > 0)
        {
        }
    }
}

public interface ITransport
{
    void Send(OutboxMessage message);
}

public sealed class TransportException(string message) : Exception(message);