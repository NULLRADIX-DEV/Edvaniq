using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Messaging;

// Runs a durable consumer for every registered handler, named <service>-<handler>. A message is handled in a scope of
// its own: the inbox entry and whatever the handler changed in the DbContext are stored by one SaveChanges, and only
// after that does the transport confirm the message to the broker. A message the inbox knows is only confirmed, so a
// redelivery has no second effect. Each message runs in an activity below the trace of its sender (MessageTrace), and
// a log line with that TraceId says when its effect is stored.
internal sealed class ConsumerHost(
    IServiceScopeFactory scopes,
    MessagingStore store,
    IEnumerable<EventSubscription> subscriptions,
    IMessageTransport transport,
    IOptions<MessagingOptions> options,
    TimeProvider time,
    ILogger<ConsumerHost> logger) : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var all = subscriptions.ToList();
        if (all.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!transport.IsConfigured())
        {
            MessagingLog.NoBroker(logger);
            return Task.CompletedTask;
        }

        return Task.WhenAll(all.Select(subscription => Task.Run(() => RunAsync(subscription, stoppingToken), CancellationToken.None)));
    }

    internal string ConsumerName(EventSubscription subscription) =>
        $"{options.Value.Service}-{new string(subscription.HandlerType.Name.Where(char.IsLetterOrDigit).ToArray())}";

    // Starts the consumer again after a failure. While the stream of the sender does not exist yet, it says so once and
    // then waits quietly: a sender that sends nothing so far is no fault.
    private async Task RunAsync(EventSubscription subscription, CancellationToken stoppingToken)
    {
        var consumer = ConsumerName(subscription);
        var spec = new Subscription(subscription.Route.Stream, consumer, subscription.Route.Subject);
        var waitingForStream = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await transport.ConsumeAsync(
                    spec,
                    (message, token) =>
                    {
                        waitingForStream = false;
                        return HandleAsync(subscription, consumer, message, token);
                    },
                    stoppingToken);
            }
            catch (StreamMissingException missing) when (!stoppingToken.IsCancellationRequested)
            {
                if (!waitingForStream)
                {
                    MessagingLog.WaitingForStream(logger, consumer, missing.Stream);
                    waitingForStream = true;
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                waitingForStream = false;
                MessagingLog.ConsumerFailed(logger, exception, consumer, RestartDelay);
            }

            try
            {
                await Task.Delay(RestartDelay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task HandleAsync(
        EventSubscription subscription, string consumer, IncomingMessage message, CancellationToken cancellationToken)
    {
        using var activity = MessageTrace.StartConsumer(consumer, message.TraceParent);
        using var logScope = MessagingLog.MessageScope(logger, consumer, message.MessageId);
        using var scope = scopes.CreateScope();
        var db = store.Resolve(scope.ServiceProvider);

        // The database calls are synchronous on purpose, MySql.Data's OpenAsync can hang forever (see MySqlHealthCheck).
        if (IsKnown(db, consumer, message.MessageId))
        {
            MessagingLog.AlreadyHandled(logger, consumer, message.MessageId);
            return;
        }

        db.Set<InboxMessage>().Add(new InboxMessage
        {
            MessageId = message.MessageId,
            Consumer = consumer,
            ReceivedAt = time.GetUtcNow().UtcDateTime,
        });
        await subscription.HandleAsync(scope.ServiceProvider, message.Payload, cancellationToken);

        try
        {
            db.SaveChanges();
            MessagingLog.Handled(logger, consumer, message.MessageId);
        }
        catch (DbUpdateException) when (WasStoredMeanwhile(consumer, message.MessageId))
        {
            // A second process got there first: it stored the effect together with the entry, ours is dropped with it.
            MessagingLog.AlreadyHandled(logger, consumer, message.MessageId);
        }
    }

    private static bool IsKnown(DbContext db, string consumer, Guid messageId) =>
        db.Set<InboxMessage>().Any(entry => entry.MessageId == messageId && entry.Consumer == consumer);

    private bool WasStoredMeanwhile(string consumer, Guid messageId)
    {
        using var scope = scopes.CreateScope();
        return IsKnown(store.Resolve(scope.ServiceProvider), consumer, messageId);
    }
}