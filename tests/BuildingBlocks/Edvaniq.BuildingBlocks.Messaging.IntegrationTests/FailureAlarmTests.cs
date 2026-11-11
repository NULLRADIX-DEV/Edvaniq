using System.Text.Json;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Sender.Contracts;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// #235 against a real MySQL: an event the handler keeps failing at turns the messaging check unhealthy after the alarm
// time, naming consumer and event type only. Fixed, the next delivery handles it and the check is healthy again.
public sealed class FailureAlarmTests(MySqlTestServer mysql) : IAsyncLifetime
{
    private const string Consumer = "receiver-GreetedHandler";

    private const string Secret = "Grüße an Lena aus der 7b";

    private readonly InMemoryTransport transport = new() { RedeliveryDelay = TimeSpan.FromMilliseconds(20) };
    private readonly HandlerBehavior behavior = new();
    private string connectionString = "";
    private TestService<ReceiverDbContext>? receiver;

    public async ValueTask InitializeAsync() => connectionString = await mysql.CreateDatabaseAsync("alarm");

    public async ValueTask DisposeAsync()
    {
        if (receiver is not null)
        {
            await receiver.DisposeAsync();
        }
    }

    [Fact]
    public async Task EventThatKeepsFailing_TurnsUnhealthy_NamingConsumerAndTypeOnly_AndHealsAfterTheFix()
    {
        behavior.Broken = Secret;
        await StartReceiverAsync(TimeSpan.FromMilliseconds(300));

        var messageId = await PublishAsync(Secret);

        await Wait.UntilAsync(
            () => HealthAsync().GetAwaiter().GetResult().Status == HealthStatus.Unhealthy,
            "The check did not turn unhealthy.",
            () => receiver!.Logs);
        var unhealthy = await HealthAsync();
        Assert.Equal($"{Consumer} cannot handle Greeted for 0 min", unhealthy.Description);
        Assert.DoesNotContain(messageId.ToString(), unhealthy.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Lena", unhealthy.Description, StringComparison.Ordinal);
        Assert.Null(unhealthy.Exception);

        // The fix is deployed: the broker delivers again, and this time it works.
        behavior.Broken = null;

        await Wait.UntilAsync(() => Inbox(messageId), "The event was not handled after the fix.", () => receiver!.Logs);
        Assert.Equal(HealthStatus.Healthy, (await HealthAsync()).Status);
    }

    [Fact]
    public async Task FailureWithinTheAlarmTime_StaysHealthy()
    {
        behavior.Broken = Secret;
        await StartReceiverAsync(TimeSpan.FromHours(1));

        await PublishAsync(Secret);

        await Wait.UntilAsync(() => behavior.Calls >= 3, "The event was not delivered again.", () => receiver!.Logs);
        Assert.Equal(HealthStatus.Healthy, (await HealthAsync()).Status);
    }

    // A sender's event that isn't ready yet is expected and dropped on purpose after a while, no alarm for it.
    [Fact]
    public async Task EventThatIsNotReady_RaisesNoAlarm()
    {
        behavior.NeverReady = "Später";
        await StartReceiverAsync(TimeSpan.Zero, notReadyDeliveries: 3);

        await PublishAsync("Später");
        await Wait.UntilAsync(() => behavior.Calls >= 2, "The event was not delivered again.", () => receiver!.Logs);
        Assert.Equal(HealthStatus.Healthy, (await HealthAsync()).Status);

        var marker = await PublishAsync("Marker");
        await Wait.UntilAsync(() => Inbox(marker), "The consumer did not get past the message.", () => receiver!.Logs);
        Assert.Equal(HealthStatus.Healthy, (await HealthAsync()).Status);
    }

    private async Task StartReceiverAsync(TimeSpan alarmAfter, int notReadyDeliveries = 360)
    {
        receiver = TestService<ReceiverDbContext>.Create(
            "receiver",
            connectionString,
            options: messaging =>
            {
                messaging.FailureAlarmAfter = alarmAfter;
                messaging.NotReadyDeliveries = notReadyDeliveries;
            },
            configure: services =>
            {
                services.AddInMemoryTransport(transport);
                services.AddSingleton(behavior);
                services.AddIntegrationEventHandler<Greeted, GreetedHandler>();
            });
        await receiver.StartAsync();
    }

    private async Task<HealthReportEntry> HealthAsync()
    {
        var report = await receiver!.Host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "messaging", TestContext.Current.CancellationToken);
        return report.Entries["messaging"];
    }

    private async Task<Guid> PublishAsync(string text)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new Greeted(Guid.NewGuid(), text), EventJson.Options);
        await transport.PublishAsync(new OutgoingMessage("sender.greeted", id, payload), TestContext.Current.CancellationToken);
        return id;
    }

    private bool Inbox(Guid messageId) =>
        receiver!.Query(db => db.Set<InboxMessage>().Any(m => m.MessageId == messageId && m.Consumer == Consumer));
}