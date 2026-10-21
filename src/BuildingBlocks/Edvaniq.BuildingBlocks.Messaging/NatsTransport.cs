using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace Edvaniq.BuildingBlocks.Messaging;

// NATS JetStream (docs/decisions/broker.md). The messages go as strings, the JSON of the outbox as it is.
internal sealed class NatsTransport : IMessageTransport, IAsyncDisposable, IDisposable
{
    private const string MessageIdHeader = "Nats-Msg-Id";

    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly NatsClient client;
    private readonly INatsJSContext jetStream;
    private readonly MessagingOptions options;
    private readonly ILogger<NatsTransport> logger;

    // The client connects with the first call, and again by itself when the connection drops.
    public NatsTransport(string url, IOptions<MessagingOptions> options, ILogger<NatsTransport> logger)
    {
        client = new NatsClient(new NatsOpts { Url = url, RetryOnInitialConnect = true });
        jetStream = client.CreateJetStreamContext();
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task EnsureStreamAsync(StreamSpec stream, CancellationToken cancellationToken) =>
        await jetStream.CreateOrUpdateStreamAsync(
            new StreamConfig(stream.Name, [stream.Subjects])
            {
                Storage = StreamConfigStorage.File,
                MaxAge = options.StreamMaxAge,
                MaxBytes = options.StreamMaxBytes,
            },
            cancellationToken);

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishTimeout);

        var ack = await jetStream.PublishAsync(
            message.Subject,
            message.Payload,
            opts: new NatsJSPubOpts { MsgId = message.MessageId.ToString() },
            cancellationToken: timeout.Token);

        // A message the stream knows already counts as sent. EnsureSuccess would throw for it.
        if (!ack.Duplicate)
        {
            ack.EnsureSuccess();
        }
    }

    public async Task ConsumeAsync(
        Subscription subscription,
        Func<IncomingMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            subscription.Stream,
            new ConsumerConfig(subscription.Consumer)
            {
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                AckWait = options.AckWait,
                FilterSubject = subscription.Subject,
            },
            cancellationToken);

        // Few messages at a time: the ack wait of the broker runs from the delivery on, not from our turn.
        await foreach (var message in consumer.ConsumeAsync<string>(
                           opts: new NatsJSConsumeOpts { MaxMsgs = 10 }, cancellationToken: cancellationToken))
        {
            if (!TryGetMessageId(message.Headers, out var messageId) || message.Data is null)
            {
                MessagingLog.MissingMessageId(logger, subscription.Consumer);
                await message.AckTerminateAsync(cancellationToken: cancellationToken);
                continue;
            }

            try
            {
                await onMessage(new IncomingMessage(messageId, message.Data), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                MessagingLog.HandlerFailed(logger, exception, subscription.Consumer, messageId);
                await message.NakAsync(new AckOpts { NakDelay = RetryDelay }, cancellationToken);
                continue;
            }

            await message.AckAsync(cancellationToken: cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static bool TryGetMessageId(NatsHeaders? headers, out Guid messageId)
    {
        messageId = Guid.Empty;
        return headers is not null
            && headers.TryGetValue(MessageIdHeader, out var value)
            && Guid.TryParse(value.ToString(), out messageId);
    }
}