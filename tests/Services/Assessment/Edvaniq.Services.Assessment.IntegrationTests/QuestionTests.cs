using System.Text.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Services.Content.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// ItemsGenerated from Content's worker leaves its questions with Assessment, each under its user and topic, and each
// only once however often it arrives (docs/spec/events.md).
public sealed class QuestionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-ItemsGeneratedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task Questions_BelongToTheUserAndTopicOfTheEvent()
    {
        var generated = await GeneratedAsync(NewUser("alice"), topicPosition: 2, Item(), Item());

        await DeliverAsync(Payload(generated));

        var stored = QuestionsOf(generated.UserId).OrderBy(question => question.Id).ToList();
        Assert.Equal(generated.Items.Select(item => item.ItemId).Order(), stored.Select(question => question.Id));
        Assert.All(stored, question => Assert.Equal(
            (generated.UserId, generated.TopicId, generated.DocumentId, 2),
            (question.OwnerId, question.TopicId, question.DocumentId, question.TopicPosition)));

        var item = generated.Items[0];
        var question = stored.Single(entry => entry.Id == item.ItemId);
        Assert.Equal(
            (item.SectionId, item.ModelId, item.PromptVersion),
            (question.SectionId, question.ModelId, question.PromptVersion));
        Assert.Equal(
            (item.Question!.Text, item.Question.Answer, item.Question.Explanation, item.Question.Difficulty),
            (question.Text, question.Answer, question.Explanation, question.Difficulty));
        Assert.Equal(item.Question.Options, question.Options);
        Assert.Equal(DateTimeKind.Utc, question.CreatedAt.Kind);
    }

    // Two topics of one document and the topic of another user stay apart.
    [Fact]
    public async Task QuestionsOfTwoTopicsAndTwoUsers_KeepTheirOwnTopic()
    {
        var alice = NewUser("alice");
        var first = await GeneratedAsync(alice, topicPosition: 0, Item());
        var second = await GeneratedAsync(alice, topicPosition: 1, Item()) with { DocumentId = first.DocumentId };
        var bob = await GeneratedAsync(NewUser("bob"), topicPosition: 0, Item());

        await DeliverAsync(Payload(first));
        await DeliverAsync(Payload(second));
        await DeliverAsync(Payload(bob));

        Assert.Equal(
            [(first.Items[0].ItemId, 0), (second.Items[0].ItemId, 1)],
            QuestionsOf(alice).OrderBy(question => question.TopicPosition).Select(question => (question.Id, question.TopicPosition)));
        Assert.Equal(bob.Items[0].ItemId, Assert.Single(QuestionsOf(bob.UserId)).Id);
    }

    // A redelivery is dropped by the inbox, the same items under a new message id by the handler.
    [Fact]
    public async Task ItemsDeliveredTwice_OrSentTwice_StoreEachQuestionOnce()
    {
        var generated = await GeneratedAsync(NewUser("alice"), topicPosition: 0, Item(), Item());
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(Payload(generated), messageId);
            await PublishAsync(Payload(generated), messageId);
            var again = await PublishAsync(Payload(generated));

            // One consumer, in order: once the second event is through, so is the duplicate.
            await WaitForAsync(() => InboxEntries(again) == 1, "The second event was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Equal(2, QuestionsOf(generated.UserId).Count);
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // What the losing side of two deliveries at once finds on its retry: the question is there, the event is confirmed
    // and only the missing question is added.
    [Fact]
    public async Task ItemsWhoseQuestionIsThereAlready_AreConfirmedWithoutASecondQuestion()
    {
        var user = NewUser("alice");
        var known = Item();
        await service.AddQuestionAsync(user, known.ItemId);
        var generated = await GeneratedAsync(user, topicPosition: 0, known, Item());

        await DeliverAsync(Payload(generated));

        var stored = QuestionsOf(user);
        Assert.Equal(2, stored.Count);
        Assert.Equal(Seed.Now, stored.Single(question => question.Id == known.ItemId).CreatedAt);
    }

    // A broken item is left out, the message still goes through and the rest of the topic arrives.
    [Fact]
    public async Task BrokenItem_DoesNotHoldUpTheOthers()
    {
        var good = Item();
        var broken = Item() with { Question = Item().Question! with { Answer = 9 } };
        var generated = await GeneratedAsync(NewUser("alice"), topicPosition: 0, broken, good);

        await DeliverAsync(Payload(generated));

        Assert.Equal(good.ItemId, Assert.Single(QuestionsOf(generated.UserId)).Id);
    }

    // The questions came before TopicsChanged. Nothing is stored until the topic is known, then the next delivery
    // brings them in.
    [Fact]
    public async Task QuestionsOfATopicNotKnownYet_ArriveOnceTheTopicIsKnown()
    {
        var user = NewUser("alice");
        var generated = new ItemsGenerated(user, Guid.NewGuid(), 0, [Item()], Guid.NewGuid());

        var messageId = await PublishAsync(Payload(generated));
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Empty(QuestionsOf(user));
        Assert.Equal(0, InboxEntries(messageId));

        await service.AddKnownTopicAsync(user, generated.TopicId!.Value);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The items were not handled once the topic was known.");
        Assert.Equal(generated.TopicId, Assert.Single(QuestionsOf(user)).TopicId);
    }

    // Sent before Content knew the topic ids: no topic to put them to, the message is confirmed and dropped.
    [Fact]
    public async Task ItemsWithoutATopic_AreDropped()
    {
        var user = NewUser("alice");

        await DeliverAsync(Payload(new ItemsGenerated(user, Guid.NewGuid(), 0, [Item()])));

        Assert.Empty(QuestionsOf(user));
    }

    // Content may add fields later, a field this service doesn't know is ignored.
    [Fact]
    public async Task ItemsWithANewField_AreStoredAnyway()
    {
        var generated = await GeneratedAsync(NewUser("alice"), topicPosition: 0, Item());
        var payload = JsonNode.Parse(Payload(generated))!.AsObject();
        payload["cards"] = new JsonArray();

        await DeliverAsync(payload.ToJsonString());

        Assert.Single(QuestionsOf(generated.UserId));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    // Items of a new topic the user has, known from TopicsChanged.
    private async Task<ItemsGenerated> GeneratedAsync(string user, int topicPosition, params GeneratedItem[] items)
    {
        var topicId = Guid.NewGuid();
        await service.AddKnownTopicAsync(user, topicId);
        return new(user, Guid.NewGuid(), topicPosition, items, topicId);
    }

    private static GeneratedItem Item() =>
        new(Guid.NewGuid(), ItemKind.Question, Guid.NewGuid(), Guid.NewGuid(), "simulated", "questions-1",
            new GeneratedQuestion("Wie heißt die Hauptstadt von Frankreich?", ["Berlin", "Paris", "Rom", "Madrid"], 1,
                "Paris ist seit dem Mittelalter die Hauptstadt.", 1));

    // Like Content's outbox would write it.
    private static string Payload(ItemsGenerated generated) => JsonSerializer.Serialize(generated, JsonSerializerOptions.Web);

    private async Task DeliverAsync(string payload)
    {
        var messageId = await PublishAsync(payload);
        await WaitForAsync(() => InboxEntries(messageId) == 1, "The items were not handled.");
    }

    private async Task<Guid> PublishAsync(string payload, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(ItemsGenerated)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private List<Question> QuestionsOf(string user) =>
        Query(db => db.Questions.IgnoreQueryFilters().AsNoTracking().Where(question => question.OwnerId == user).ToList());

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