using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Alpha.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// What AddMessaging puts into a service, and that it stays quiet without a broker.
public class MessagingRegistrationTests
{
    [Fact]
    public void WithoutAnAddress_NoBrokerIsConfigured()
    {
        using var host = Build();

        Assert.False(host.Services.GetRequiredService<IMessageTransport>().IsConfigured());
    }

    [Fact]
    public void WithAnAddress_TheBrokerIsNats()
    {
        using var host = Build(address: "nats://localhost:4222");

        Assert.IsType<NatsTransport>(host.Services.GetRequiredService<IMessageTransport>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InMemoryTransport_ReplacesTheBroker_InEitherOrder(bool beforeMessaging)
    {
        var transport = new InMemoryTransport();
        using var host = Build(address: "nats://localhost:4222", configure: services =>
        {
            if (beforeMessaging)
            {
                services.AddInMemoryTransport(transport);
            }
        }, afterMessaging: services =>
        {
            if (!beforeMessaging)
            {
                services.AddInMemoryTransport(transport);
            }
        });

        Assert.Same(transport, host.Services.GetRequiredService<IMessageTransport>());
        Assert.Same(transport, host.Services.GetRequiredService<InMemoryTransport>());
    }

    [Fact]
    public void Outbox_SharesTheDbContextOfItsScope()
    {
        using var host = Build(service: "alpha");
        using var scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventOutbox>().Add(new Pinged(Guid.NewGuid(), "Hallo"));

        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        Assert.Single(db.ChangeTracker.Entries<OutboxMessage>());
    }

    [Fact]
    public void Handler_GetsADurableConsumerNamedAfterServiceAndHandler()
    {
        using var host = Build(configure: services => services.AddIntegrationEventHandler<Pinged, PingedHandler>());

        using var scope = host.Services.CreateScope();

        var subscription = Assert.Single(host.Services.GetServices<EventSubscription>());
        var consumers = host.Services.GetServices<IHostedService>().OfType<ConsumerHost>().Single();

        Assert.Equal(new EventRoute("alpha", "ALPHA", "alpha.pinged"), subscription.Route);
        Assert.Equal("beta-PingedHandler", consumers.ConsumerName(subscription));
        Assert.NotNull(scope.ServiceProvider.GetService<PingedHandler>());
    }

    [Fact]
    public async Task WithoutABroker_TheServiceStartsAndStopsWithoutTouchingTheDatabase()
    {
        using var host = Build(configure: services => services.AddIntegrationEventHandler<Pinged, PingedHandler>());

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    // The dispatcher fails on the unreachable database, says so in the log and keeps trying, and stops when the host does.
    [Fact]
    public async Task WithABrokerButNoDatabase_TheServiceStaysUpAndStopsInTime()
    {
        var logs = new LogCollector();
        using var host = Build(
            afterMessaging: services => services.AddInMemoryTransport(),
            logs: logs);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await Wait.UntilAsync(() => logs.Messages.Any(message => message.Contains("Outbox dispatch failed")), "The failure was not logged.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await host.StopAsync(timeout.Token);
    }

    private static IHost Build(
        string service = "beta",
        string? address = null,
        Action<IServiceCollection>? configure = null,
        Action<IServiceCollection>? afterMessaging = null,
        LogCollector? logs = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        if (address is not null)
        {
            builder.Configuration["ConnectionStrings:nats"] = address;
        }

        builder.Services.AddDbContext<TestDbContext>(options => options.UseMySQL(
            Edvaniq.BuildingBlocks.Infrastructure.MySqlConnections.For(TestDbContext.UnreachableDatabase).ConnectionString));
        configure?.Invoke(builder.Services);
        builder.Services.AddMessaging<TestDbContext>(service);
        afterMessaging?.Invoke(builder.Services);
        return builder.Build();
    }

    private sealed class PingedHandler : IIntegrationEventHandler<Pinged>
    {
        public Task HandleAsync(Pinged integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class LogCollector : ILoggerProvider
    {
        private readonly List<string> messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (messages)
                {
                    return [.. messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Collector(this);

        public void Dispose()
        {
        }

        private sealed class Collector(LogCollector owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.messages)
                {
                    owner.messages.Add(formatter(state, exception));
                }
            }
        }
    }
}