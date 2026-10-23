using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.ServiceName.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

// UserDeleted removes all of the user's data and nobody else's. Keep these when the example goes, with your own data.
public sealed class UserDeletionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-UserDeletedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task DeletedUser_LosesAllTheirData_OthersKeepTheirs()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await CreateItemAsync(alice, "Notiz eins");
        await CreateItemAsync(alice, "Notiz zwei");
        await CreateItemAsync(bob, "Notiz von Bob");

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.Equal(0, ItemsOf(alice));
        Assert.Equal(1, ItemsOf(bob));
    }

    // "Alice" and "alice" are two users, deleting one keeps the data of the other.
    [Fact]
    public async Task DeletedUser_KeepsTheDataOfTheSameNameInOtherCase()
    {
        var id = Guid.NewGuid().ToString("N");
        var lower = $"alice-{id}";
        var upper = $"Alice-{id}";
        await CreateItemAsync(lower, "Notiz klein");
        await CreateItemAsync(upper, "Notiz gross");

        var messageId = await DeleteAsync(upper);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.Equal(0, ItemsOf(upper));
        Assert.Equal(1, ItemsOf(lower));
    }

    // A redelivery and a second deletion of the same user both do nothing.
    [Fact]
    public async Task DeletionDeliveredTwice_OrSentTwice_DoesNoHarm()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await CreateItemAsync(alice, "Notiz");
        await CreateItemAsync(bob, "Notiz von Bob");
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await DeleteAsync(alice, messageId);
            await DeleteAsync(alice, messageId);
            var again = await DeleteAsync(alice);

            // One consumer, in order: once the second deletion is through, so is the duplicate.
            await WaitForAsync(() => InboxEntries(again) == 1, "The second deletion was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Equal(0, ItemsOf(alice));
            Assert.Equal(1, ItemsOf(bob));
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // Proof per service: the inbox entry and a log line after the commit with the deleting request's TraceId.
    [Fact]
    public async Task Deletion_IsTraceable_ByItsInboxEntryAndALogLineWithTheTraceIdOfTheRequest()
    {
        var alice = NewUser("alice");
        await CreateItemAsync(alice, "Notiz");
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        var messageId = await DeleteAsync(alice, traceParent: $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        await WaitForAsync(
            () => service.Logs.Entries.Any(entry => entry.Message == $"Consumer {Consumer} has handled message {messageId}" && entry.TraceId == traceId),
            "No log line says with the TraceId of the request that the deletion was handled.");
        Assert.Equal(1, InboxEntries(messageId));
        Assert.Contains(service.Logs.Entries, entry => entry.Message == "Rows of a deleted user to remove: 1" && entry.TraceId == traceId);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private async Task CreateItemAsync(string user, string text)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));

        using var response = await client.PostAsJsonAsync("/examples", new { text }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Like Identity's dispatcher would.
    private async Task<Guid> DeleteAsync(string user, Guid? messageId = null, string? traceParent = null)
    {
        var id = messageId ?? Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new UserDeleted(user), JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(UserDeleted)).Subject, id, payload, traceParent),
            TestContext.Current.CancellationToken);
        return id;
    }

    private int ItemsOf(string user) =>
        Query(db => db.ExampleItems.IgnoreQueryFilters().Count(item => item.OwnerId == user));

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<ServiceNameDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ServiceNameDbContext>());
    }

    // The consumer runs in the background.
    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}