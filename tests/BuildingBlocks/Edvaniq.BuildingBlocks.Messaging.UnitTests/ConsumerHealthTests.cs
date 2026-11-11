using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Alpha.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// #235: a message a consumer can't handle turns /health unhealthy after a while, naming consumer and event type only.
public class ConsumerHealthTests
{
    private static readonly MessagingOptions Options = new() { FailureAlarmAfter = TimeSpan.FromMinutes(5) };

    [Fact]
    public async Task NoFailure_IsHealthy()
    {
        var (check, _, _) = Check();

        Assert.Equal(HealthStatus.Healthy, (await RunAsync(check)).Status);
    }

    [Fact]
    public async Task FailureYoungerThanTheAlarmTime_IsHealthy()
    {
        var (check, failures, clock) = Check();
        failures.Failed("beta-PingedHandler", "Pinged", Guid.NewGuid());

        clock.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(HealthStatus.Healthy, (await RunAsync(check)).Status);
    }

    [Fact]
    public async Task FailureOlderThanTheAlarmTime_IsUnhealthy_NamingConsumerAndTypeOnly()
    {
        var (check, failures, clock) = Check();
        var messageId = Guid.NewGuid();
        failures.Failed("beta-PingedHandler", "Pinged", messageId);
        clock.Advance(TimeSpan.FromMinutes(2));
        failures.Failed("beta-PingedHandler", "Pinged", Guid.NewGuid());

        clock.Advance(TimeSpan.FromMinutes(6));
        var result = await RunAsync(check);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("beta-PingedHandler cannot handle Pinged for 8 min", result.Description);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(messageId.ToString(), result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TwoConsumers_AreBothNamed()
    {
        var (check, failures, clock) = Check();
        failures.Failed("beta-PingedHandler", "Pinged", Guid.NewGuid());
        failures.Failed("beta-OtherHandler", "Other", Guid.NewGuid());

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(
            "beta-OtherHandler cannot handle Other for 5 min; beta-PingedHandler cannot handle Pinged for 5 min",
            (await RunAsync(check)).Description);
    }

    // Once the message works, or is dropped on purpose, the alarm goes.
    [Fact]
    public async Task SettledMessage_IsHealthyAgain()
    {
        var (check, failures, clock) = Check();
        var messageId = Guid.NewGuid();
        failures.Failed("beta-PingedHandler", "Pinged", messageId);
        clock.Advance(TimeSpan.FromMinutes(10));

        failures.Settled("beta-PingedHandler", messageId);

        Assert.Equal(HealthStatus.Healthy, (await RunAsync(check)).Status);
    }

    // The same message failing again keeps its first time, the alarm doesn't start over with each delivery.
    [Fact]
    public async Task RepeatedFailure_KeepsTheFirstTime()
    {
        var (check, failures, clock) = Check();
        var messageId = Guid.NewGuid();
        failures.Failed("beta-PingedHandler", "Pinged", messageId);
        clock.Advance(TimeSpan.FromMinutes(3));
        failures.Failed("beta-PingedHandler", "Pinged", messageId);
        clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(HealthStatus.Unhealthy, (await RunAsync(check)).Status);
    }

    // A failing handler counts, with the event's name. The database is unreachable here, so the inbox lookup fails like
    // a handler would.
    [Fact]
    public async Task FailedDelivery_IsCounted_WithTheEventName()
    {
        using var host = Host();
        var consumers = host.Services.GetServices<IHostedService>().OfType<ConsumerHost>().Single();
        var subscription = host.Services.GetServices<EventSubscription>().Single();
        var messageId = Guid.NewGuid();

        await Assert.ThrowsAnyAsync<Exception>(() => consumers.HandleAsync(
            subscription, "beta-PingedHandler", new IncomingMessage(messageId, "{}"), TestContext.Current.CancellationToken));

        var failure = Assert.Single(host.Services.GetRequiredService<ConsumerFailures>().Current());
        Assert.Equal(("beta-PingedHandler", "Pinged"), (failure.Consumer, failure.EventType));
    }

    // AddMessaging brings the check along, tagged so /health shows its description.
    [Fact]
    public void AddMessaging_RegistersTheCheck()
    {
        using var host = Host();

        var registration = Assert.Single(
            host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            registration => registration.Name == "messaging");
        Assert.Contains("details", registration.Tags);
    }

    private static IHost Host()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<TestDbContext>(options => options.UseMySQL(
            Edvaniq.BuildingBlocks.Infrastructure.MySqlConnections.For(TestDbContext.UnreachableDatabase).ConnectionString));
        builder.Services.AddIntegrationEventHandler<Pinged, PingedHandler>();
        builder.Services.AddMessaging<TestDbContext>("beta");
        return builder.Build();
    }

    private static (ConsumerHealthCheck Check, ConsumerFailures Failures, ManualClock Clock) Check()
    {
        var clock = new ManualClock();
        var failures = new ConsumerFailures(clock);
        return (new ConsumerHealthCheck(failures, Microsoft.Extensions.Options.Options.Create(Options), clock), failures, clock);
    }

    private static Task<HealthCheckResult> RunAsync(ConsumerHealthCheck check) =>
        check.CheckHealthAsync(
            new HealthCheckContext { Registration = new HealthCheckRegistration("messaging", check, HealthStatus.Unhealthy, ["details"]) },
            TestContext.Current.CancellationToken);

    private sealed class PingedHandler : IIntegrationEventHandler<Pinged>
    {
        public Task HandleAsync(Pinged integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}