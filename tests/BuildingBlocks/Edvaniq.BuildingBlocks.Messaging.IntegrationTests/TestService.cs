using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// A service in this process: its DbContext on a database of its own, messaging as AddMessaging sets it up, with short
// intervals so the tests do not wait long. It has no broker unless the test gives it a NATS address or swaps the transport.
internal sealed class TestService<TContext> : IAsyncDisposable
    where TContext : DbContext
{
    private readonly LogCollector logs;
    private bool started;

    private TestService(IHost host, LogCollector logs)
    {
        Host = host;
        this.logs = logs;
    }

    public IHost Host { get; }

    public string Logs => logs.Text;

    public OutboxDispatcher Dispatcher => Host.Services.GetServices<IHostedService>().OfType<OutboxDispatcher>().Single();

    public MessagingCleanup Cleanup => Host.Services.GetServices<IHostedService>().OfType<MessagingCleanup>().Single();

    public static TestService<TContext> Create(
        string name,
        string connectionString,
        string? natsUrl = null,
        Action<IServiceCollection>? configure = null,
        Action<MessagingOptions>? options = null)
    {
        var logs = new LogCollector();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        if (natsUrl is not null)
        {
            builder.Configuration["ConnectionStrings:nats"] = natsUrl;
        }

        builder.Services.AddDbContext<TContext>(db => db.UseMySQL(MySqlConnections.For(connectionString).ConnectionString));
        builder.Services.AddMessaging<TContext>(name);
        builder.Services.Configure<MessagingOptions>(messaging =>
        {
            messaging.DispatchInterval = TimeSpan.FromMilliseconds(100);
            messaging.AckWait = TimeSpan.FromSeconds(2);
            options?.Invoke(messaging);
        });
        configure?.Invoke(builder.Services);

        var service = new TestService<TContext>(builder.Build(), logs);
        using var scope = service.Host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreated();
        return service;
    }

    // Starts the dispatcher, the consumers and the cleanup, as a running service does.
    public async Task StartAsync()
    {
        await Host.StartAsync(TestContext.Current.CancellationToken);
        started = true;
    }

    public IServiceScope Scope() => Host.Services.CreateScope();

    public T Query<T>(Func<TContext, T> query)
    {
        using var scope = Scope();
        return query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    // One unit of work: the change and its events, stored by a SaveChanges like a request does.
    public void Save(Action<TContext, IEventOutbox> change)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        change(db, scope.ServiceProvider.GetRequiredService<IEventOutbox>());
        db.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        if (started)
        {
            await Host.StopAsync(CancellationToken.None);
        }

        // Not Dispose: a service of the container may be disposable only asynchronously.
        await ((IAsyncDisposable)Host).DisposeAsync();
    }

    private sealed class LogCollector : ILoggerProvider
    {
        private readonly List<string> lines = [];

        public string Text
        {
            get
            {
                lock (lines)
                {
                    return string.Join(Environment.NewLine, lines);
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Collector(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Collector(LogCollector owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.lines)
                {
                    owner.lines.Add($"{logLevel} {category}: {formatter(state, exception)} {exception?.Message}");
                }
            }
        }
    }
}