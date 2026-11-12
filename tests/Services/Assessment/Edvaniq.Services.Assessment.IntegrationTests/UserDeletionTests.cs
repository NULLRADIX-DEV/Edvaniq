using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// UserDeleted removes all of the user's questions and attempts and nobody else's.
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
        await service.AddAttemptAsync(await service.AddQuestionAsync(alice));
        await service.AddQuestionAsync(alice);
        await service.AddAttemptAsync(await service.AddQuestionAsync(bob));

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.Equal(0, ItemsOf(alice));
        Assert.Equal(2, ItemsOf(bob));
    }

    // Every table with an owner holds rows of both users. A new table without a seed here fails before the deletion.
    [Fact]
    public async Task DeletedUser_HasNoRowInAnyTable_OthersKeepTheirs()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await SeedEveryTableAsync(alice);
        await SeedEveryTableAsync(bob);
        Assert.Empty(OwnedRows.Unseeded(Query(db => OwnedRows.Of(db, alice))));
        var ofBob = Query(db => OwnedRows.Of(db, bob));

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.All(Query(db => OwnedRows.Of(db, alice)), table => Assert.Equal(0, table.Value));
        Assert.Equal(ofBob, Query(db => OwnedRows.Of(db, bob)));
    }

    private async Task SeedEveryTableAsync(string user)
    {
        await service.AddAttemptAsync(await service.AddQuestionAsync(user));
        var topic = Guid.NewGuid();
        await service.AddKnownTopicAsync(user, topic);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        db.TopicsSnapshots.Add(TopicsSnapshot.Create(user, Guid.Empty, Seed.Now));
        db.DeletedDocuments.Add(DeletedDocument.Of(user, Guid.NewGuid(), Seed.Now));
        var quiz = BlockQuiz.Create(user, Guid.NewGuid(), topic, Seed.Now);
        db.BlockQuizzes.Add(quiz);
        db.Attempts.Add(Attempt.AskIn(quiz, await service.AddQuestionAsync(user), Seed.Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // "Alice" and "alice" are two users, deleting one keeps the data of the other.
    [Fact]
    public async Task DeletedUser_KeepsTheDataOfTheSameNameInOtherCase()
    {
        var id = Guid.NewGuid().ToString("N");
        var lower = $"alice-{id}";
        var upper = $"Alice-{id}";
        await service.AddQuestionAsync(lower);
        await service.AddQuestionAsync(upper);

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
        await service.AddQuestionAsync(alice);
        await service.AddQuestionAsync(bob);
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
        await service.AddQuestionAsync(alice);
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        var messageId = await DeleteAsync(alice, traceParent: $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        await WaitForAsync(
            () => service.Logs.Entries.Any(entry => entry.Message == $"Consumer {Consumer} has handled message {messageId}" && entry.TraceId == traceId),
            "No log line says with the TraceId of the request that the deletion was handled.");
        Assert.Equal(1, InboxEntries(messageId));
        Assert.Contains(service.Logs.Entries, entry => entry.Message == "Rows of a deleted user to remove: 1" && entry.TraceId == traceId);
    }

    // #953: once the deletion is through, a token issued before it is turned away like an invalid one. Others still work.
    [Fact]
    public async Task TokenOfADeletedUser_IsRejected_OthersStillWork()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        Assert.Equal(HttpStatusCode.OK, await MeAsync(alice));

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(alice));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(bob));
    }

    private async Task<HttpStatusCode> MeAsync(string user)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        using var response = await client.GetAsync("/me", TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

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

    // Questions and attempts.
    private int ItemsOf(string user) =>
        Query(db => db.Questions.IgnoreQueryFilters().Count(item => item.OwnerId == user)
            + db.Attempts.IgnoreQueryFilters().Count(item => item.OwnerId == user));

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<AssessmentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AssessmentDbContext>());
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