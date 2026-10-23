using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Alpha.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// A sender that has sent nothing yet has no stream. Its consumers wait for it, and say so once instead of warning every
// time they try again.
public class ConsumerHostTests
{
    [Fact]
    public async Task ConsumerOfAMissingStream_SaysOnceThatItWaits_AndKeepsTrying()
    {
        var transport = new MissingStream();
        var logs = new Logs();

        await RunAsync(transport, logs, until: () => transport.Attempts >= 3);

        Assert.Equal(["Consumer beta-PingedHandler waits for the stream ALPHA of its sender"], logs.Messages(LogLevel.Information));
        Assert.Empty(logs.Messages(LogLevel.Warning));
    }

    // Another failure is still a warning, and after it the consumer says again when it waits.
    [Fact]
    public async Task OtherFailures_AreWarnings()
    {
        var transport = new MissingStream { FailDifferentlyOnAttempt = 2 };
        var logs = new Logs();

        await RunAsync(transport, logs, until: () => transport.Attempts >= 4);

        Assert.Single(logs.Messages(LogLevel.Warning), message => message.StartsWith("Consumer beta-PingedHandler stopped", StringComparison.Ordinal));
        Assert.Equal(2, logs.Messages(LogLevel.Information).Count(message => message.Contains("waits for the stream", StringComparison.Ordinal)));
    }

    private static async Task RunAsync(IMessageTransport transport, Logs logs, Func<bool> until)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<TestDbContext>(options => options.UseMySQL(
            Edvaniq.BuildingBlocks.Infrastructure.MySqlConnections.For(TestDbContext.UnreachableDatabase).ConnectionString));
        builder.Services.AddIntegrationEventHandler<Pinged, PingedHandler>();
        builder.Services.AddMessaging<TestDbContext>("beta");
        using var host = builder.Build();
        using var loggers = LoggerFactory.Create(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));

        // Built here instead of started with the host, so only the consumers run, without waiting between attempts.
        var consumers = new ConsumerHost(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            host.Services.GetRequiredService<MessagingStore>(),
            host.Services.GetServices<EventSubscription>(),
            transport,
            host.Services.GetRequiredService<IOptions<MessagingOptions>>(),
            new NoWaiting(),
            loggers.CreateLogger<ConsumerHost>());

        await consumers.StartAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(until, "The consumer did not try often enough.");
        await consumers.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class PingedHandler : IIntegrationEventHandler<Pinged>
    {
        public Task HandleAsync(Pinged integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // The stream ALPHA does not exist, NatsTransport reports it this way.
    private sealed class MissingStream : IMessageTransport
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public int FailDifferentlyOnAttempt { get; init; }

        public Task EnsureStreamAsync(StreamSpec stream, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ConsumeAsync(
            Subscription subscription,
            Func<IncomingMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == FailDifferentlyOnAttempt)
            {
                throw new InvalidOperationException("The broker is gone.");
            }

            throw new StreamMissingException(subscription.Stream);
        }
    }

    // Every delay ends at once, so the consumer tries again right away.
    private sealed class NoWaiting : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            System.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
    }

    private sealed class Logs : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> entries = [];

        public IReadOnlyList<string> Messages(LogLevel level)
        {
            lock (entries)
            {
                return [.. entries.Where(entry => entry.Level == level).Select(entry => entry.Message)];
            }
        }

        public ILogger CreateLogger(string categoryName) => new Collector(this);

        public void Dispose()
        {
        }

        private sealed class Collector(Logs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.entries)
                {
                    owner.entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}