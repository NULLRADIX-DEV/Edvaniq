using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Messaging;

// Sends the events of the outbox to the broker, in rounds of one batch. It claims a batch with SELECT ... FOR UPDATE SKIP
// LOCKED, sends it and marks it as sent, all in one transaction: with several processes, each takes other rows instead of
// waiting. If the commit fails after sending, the batch goes out again, so the delivery is at least once. The
// broker drops a message it knows by its MessageId, and every inbox does the same later.
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    MessagingStore store,
    IMessageTransport transport,
    IOptions<MessagingOptions> options,
    TimeProvider time,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int MaxBackoffSeconds = 30;

    internal sealed record Round(int Sent, bool Failed);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!transport.IsConfigured())
        {
            MessagingLog.NoBroker(logger);
            return;
        }

        // The first round already works on the database, so let the host finish its start first.
        await Task.Yield();

        var settings = options.Value;
        var stream = new StreamSpec(EventRoute.StreamOf(settings.Service), EventRoute.SubjectsOf(settings.Service));
        var streamReady = false;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = settings.DispatchInterval;
            try
            {
                if (!streamReady)
                {
                    await transport.EnsureStreamAsync(stream, stoppingToken);
                    streamReady = true;
                }

                var round = await DispatchOnceAsync(stoppingToken);
                failures = round.Failed ? failures + 1 : 0;
                streamReady = !round.Failed;
                if (round.Failed)
                {
                    delay = Backoff(failures);
                }
                else if (round.Sent == settings.BatchSize)
                {
                    continue;
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                streamReady = false;
                delay = Backoff(failures);
                MessagingLog.DispatchFailed(logger, exception, delay);
            }

            try
            {
                await Task.Delay(delay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Claims, sends and marks one batch. The first message the broker does not take ends the round: the broker is
    // probably down for the rest as well, and the order stays as it was.
    internal async Task<Round> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = store.Resolve(scope.ServiceProvider);

        // The database calls are synchronous on purpose, MySql.Data's OpenAsync can hang forever (see MySqlHealthCheck).
        using var transaction = db.Database.BeginTransaction();

        var batch = db.Set<OutboxMessage>()
            .FromSql($"SELECT * FROM outbox_messages WHERE SentAt IS NULL ORDER BY Id LIMIT {options.Value.BatchSize} FOR UPDATE SKIP LOCKED")
            .ToList();

        var sent = 0;
        var failed = false;
        foreach (var message in batch)
        {
            message.Attempts++;
            try
            {
                await transport.PublishAsync(new OutgoingMessage(message.Subject, message.MessageId, message.Payload), cancellationToken);
                message.SentAt = time.GetUtcNow().UtcDateTime;
                message.LastError = null;
                sent++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                var error = exception.Message.Length > OutboxMessage.LastErrorMaxLength
                    ? exception.Message[..OutboxMessage.LastErrorMaxLength]
                    : exception.Message;
                message.LastError = error;
                MessagingLog.SendFailed(logger, message.MessageId, message.Subject, message.Attempts, error);
                failed = true;
                break;
            }
        }

        db.SaveChanges();
        transaction.Commit();
        return new Round(sent, failed);
    }

    private static TimeSpan Backoff(int failures) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoffSeconds, Math.Pow(2, failures)));
}