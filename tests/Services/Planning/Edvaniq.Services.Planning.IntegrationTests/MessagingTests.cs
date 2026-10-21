using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// Creating an item stores its event in the outbox with the item. The dispatcher sends it to the broker, here one inside the
// process, and the consumer of the example handler enters it in the inbox.
public sealed class MessagingTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task NewItem_SendsItsEventThroughTheBroker_AndTheHandlerGetsItOnce()
    {
        var itemId = await CreateItemAsync();

        var message = await WaitForAsync(() => Query(db =>
            db.Set<OutboxMessage>().AsNoTracking().SingleOrDefault(m => m.Payload.Contains(itemId.ToString()) && m.SentAt != null)));
        Assert.Equal($"{InfrastructureExtensions.ServiceKey}.exampleitemcreated", message.Subject);
        Assert.Equal(1, message.Attempts);

        var broker = service.Services.GetRequiredService<InMemoryTransport>();
        Assert.Equal(message.Subject, Assert.Single(broker.Published, m => m.MessageId == message.MessageId).Subject);

        var consumer = $"{InfrastructureExtensions.ServiceKey}-ExampleItemCreatedHandler";
        await WaitForAsync(() => Query(db =>
            db.Set<InboxMessage>().AsNoTracking().SingleOrDefault(m => m.MessageId == message.MessageId && m.Consumer == consumer)));
    }

    private async Task<Guid> CreateItemAsync()
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create($"alice-{Guid.NewGuid():N}"));

        using var response = await client.PostAsJsonAsync("/examples", new { text = "Notiz von Alice" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Item>(TestContext.Current.CancellationToken))!.Id;
    }

    private T? Query<T>(Func<PlanningDbContext, T?> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    // The dispatcher and the consumer run in the background of the service, so the result shows up after a moment.
    private static async Task<T> WaitForAsync<T>(Func<T?> read) where T : class
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            if (read() is { } result)
            {
                return result;
            }

            Assert.True(DateTime.UtcNow < deadline, "The event did not arrive in time.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private sealed record Item(Guid Id);
}