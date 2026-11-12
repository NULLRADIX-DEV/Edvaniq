using System.Text.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Mode = Edvaniq.Services.Assessment.Contracts.Mode;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// AnswerRecorded from Assessment leaves exactly one learning record per answerId (docs/spec/events.md#lern-nachweis),
// with response time and confidence kept as they came.
public sealed class LearningRecordTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-AnswerRecordedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task Answer_LeavesARecordWithEveryField()
    {
        var answer = Answer(NewUser("alice")) with { Confidence = 1, Source = Mode.Placement };

        await DeliverAsync(Payload(answer));

        var record = Assert.Single(RecordsOf(answer.UserId));
        Assert.Equal(
            (answer.AnswerId, answer.TopicId, answer.ItemId, answer.SessionId, answer.Correct, answer.Difficulty),
            (record.AnswerId, record.TopicId, record.ItemId, record.SessionId, record.Correct, record.Difficulty));
        Assert.Equal((answer.ResponseTimeMs, answer.Confidence, Domain.Mode.Placement), (record.ResponseTimeMs, record.Confidence, record.Source));
        Assert.Equal(answer.AnsweredAt, record.AnsweredAt);
        Assert.Equal(DateTimeKind.Utc, record.AnsweredAt.Kind);
    }

    // No session, no difficulty (a card) and no confidence: the optional fields stay empty.
    [Fact]
    public async Task Answer_WithoutTheOptionalFields_KeepsThemEmpty()
    {
        var answer = Answer(NewUser("alice")) with { SessionId = null, Difficulty = null, Confidence = null };

        await DeliverAsync(Payload(answer));

        var record = Assert.Single(RecordsOf(answer.UserId));
        Assert.Equal(((Guid?)null, (int?)null, (int?)null), (record.SessionId, record.Difficulty, record.Confidence));
    }

    // A redelivery is dropped by the inbox, the same answer under a new message id by the handler.
    [Fact]
    public async Task AnswerDeliveredTwice_OrSentTwice_CountsOnce()
    {
        var answer = Answer(NewUser("alice"));
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(Payload(answer), messageId);
            await PublishAsync(Payload(answer), messageId);
            var again = await PublishAsync(Payload(answer));

            // One consumer, in order: once the second event is through, so is the duplicate.
            await WaitForAsync(() => InboxEntries(again) == 1, "The second event was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Single(RecordsOf(answer.UserId));
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // What the losing side of two deliveries at once finds on its retry: the record is there, the event is confirmed.
    [Fact]
    public async Task AnswerWhoseRecordIsThereAlready_IsConfirmedWithoutASecondRecord()
    {
        var answer = Answer(NewUser("alice"));
        await service.AddRecordAsync(answer.UserId, answer.AnswerId);

        await DeliverAsync(Payload(answer));

        var record = Assert.Single(RecordsOf(answer.UserId));
        Assert.Equal(Seed.Now, record.AnsweredAt);
    }

    // A newer sender only adds optional fields, so a higher version with a field this service doesn't know is read.
    [Fact]
    public async Task Answer_OfAHigherVersionWithANewField_IsStoredAnyway()
    {
        var answer = Answer(NewUser("alice"));
        var payload = JsonNode.Parse(Payload(answer))!.AsObject();
        payload["version"] = AnswerRecorded.CurrentVersion + 1;
        payload["hintsUsed"] = 2;

        await DeliverAsync(payload.ToJsonString());

        Assert.Equal(answer.AnswerId, Assert.Single(RecordsOf(answer.UserId)).AnswerId);
    }

    [Fact]
    public async Task AnswersOfTwoUsers_EachLeaveTheirOwnRecord()
    {
        var alice = Answer(NewUser("alice"));
        var bob = Answer(NewUser("bob"));

        await DeliverAsync(Payload(alice));
        await DeliverAsync(Payload(bob));

        Assert.Equal(alice.AnswerId, Assert.Single(RecordsOf(alice.UserId)).AnswerId);
        Assert.Equal(bob.AnswerId, Assert.Single(RecordsOf(bob.UserId)).AnswerId);
    }

    // #944: an answer that comes after the user's deletion is confirmed and leaves no record.
    [Fact]
    public async Task LateAnswer_OfADeletedUser_IsConfirmedWithoutEffect()
    {
        var answer = Answer(NewUser("alice"));
        var deletion = Guid.NewGuid();
        await Broker.PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(UserDeleted)).Subject,
                deletion,
                JsonSerializer.Serialize(new UserDeleted(answer.UserId), JsonSerializerOptions.Web),
                null),
            TestContext.Current.CancellationToken);
        await WaitForAsync(
            () => Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == deletion)) == 1,
            "The deletion was not handled.");

        await DeliverAsync(Payload(answer));

        Assert.Empty(RecordsOf(answer.UserId));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static AnswerRecorded Answer(string user) =>
        new(AnswerRecorded.CurrentVersion, user, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            false, 3, 7350, 2, Mode.Practice, new DateTime(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc));

    // Like Assessment's outbox would write it.
    private static string Payload(AnswerRecorded answer) => JsonSerializer.Serialize(answer, JsonSerializerOptions.Web);

    private async Task DeliverAsync(string payload)
    {
        var messageId = await PublishAsync(payload);
        await WaitForAsync(() => InboxEntries(messageId) == 1, "The answer was not handled.");
    }

    private async Task<Guid> PublishAsync(string payload, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(AnswerRecorded)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private List<LearningRecord> RecordsOf(string user) =>
        Query(db => db.LearningRecords.IgnoreQueryFilters().AsNoTracking().Where(record => record.OwnerId == user).ToList());

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<LearningEngineDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>());
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