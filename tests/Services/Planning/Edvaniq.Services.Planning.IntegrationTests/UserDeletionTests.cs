using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// When Identity deletes a user, the service removes all their data and nothing of anyone else's
// (docs/service-template.md#nutzer-löschen). UserDeleted arrives the way the dispatcher of Identity sends it, through the
// broker inside the process. The tests stay when the example is replaced, they then create the service's own data.
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

    // The broker delivers the same message again, and Identity may send a second deletion of the same user. Neither fails,
    // neither has a second effect, and the data of others stays.
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

            // One consumer takes the messages in order, so the duplicate has been through when the second deletion has.
            // A handler that failed on it would have stopped the queue.
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

    // The proof that the service removed the data: its inbox entry, and a log line written after the commit that carries
    // the TraceId of the request that deleted the user.
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

    // The way the dispatcher of Identity publishes the event: its JSON under its subject, with the traceparent of the
    // request that stored it.
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

    private T Query<T>(Func<PlanningDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    // The consumer runs in the background of the service, so the result shows up after a moment.
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