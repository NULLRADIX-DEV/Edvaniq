using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// DocumentDeleted takes the questions of the document and every attempt on them. Questions of other documents, also on
// the same topic, and other users stay. A second delivery changes nothing, and questions of the deleted document that
// come late don't come back.
public sealed class DocumentDeletionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-DocumentDeletedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task DeletedDocument_TakesItsQuestionsAndTheirAttempts_OthersStay()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var topic = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var answered = await AddQuestionAsync(alice, topic, deleted);
        await AddQuestionAsync(alice, topic, deleted);
        var other = await AddQuestionAsync(alice, topic, kept);
        var ofBob = await AddQuestionAsync(bob, Guid.NewGuid(), deleted);
        await AnswerAsync(answered);
        await service.AddAttemptAsync(answered);
        await service.AddAttemptAsync(other);
        await service.AddAttemptAsync(ofBob);

        await DeliverAsync(new DocumentDeleted(alice, deleted, Guid.NewGuid()));

        Assert.Equal([other.Id], Query(db => db.Questions.IgnoreQueryFilters().Where(question => question.OwnerId == alice).Select(question => question.Id).ToList()));
        Assert.Equal([other.Id], Query(db => db.Attempts.IgnoreQueryFilters().Where(attempt => attempt.OwnerId == alice).Select(attempt => attempt.QuestionId).ToList()));
        Assert.Equal((1, 1), (QuestionsOf(bob), Query(db => db.Attempts.IgnoreQueryFilters().Count(attempt => attempt.OwnerId == bob))));
    }

    // The same message twice, and the same event under a new id.
    [Fact]
    public async Task DeletionDeliveredTwice_OrSentTwice_ChangesNothingMore()
    {
        var alice = NewUser("alice");
        var deleted = Guid.NewGuid();
        await AddQuestionAsync(alice, Guid.NewGuid(), deleted);
        var kept = await AddQuestionAsync(alice, Guid.NewGuid(), Guid.NewGuid());
        var deletion = new DocumentDeleted(alice, deleted);
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(deletion, messageId);
            await PublishAsync(deletion, messageId);
            var again = await PublishAsync(deletion);

            await WaitForAsync(() => InboxEntries(again) == 1, "The second deletion was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Equal([kept.Id], Query(db => db.Questions.IgnoreQueryFilters().Where(question => question.OwnerId == alice).Select(question => question.Id).ToList()));
            Assert.Equal(1, Query(db => db.DeletedDocuments.IgnoreQueryFilters().Count(document => document.OwnerId == alice)));
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // DocumentDeleted overtook the questions on its own consumer.
    [Fact]
    public async Task QuestionsAfterTheDeletion_DontComeBack()
    {
        var alice = NewUser("alice");
        var topic = Guid.NewGuid();
        var document = Guid.NewGuid();
        await service.AddKnownTopicAsync(alice, topic);

        await DeliverAsync(new DocumentDeleted(alice, document));
        await DeliverAsync(new ItemsGenerated(alice, document, 0, [Item(document), Item(document)], topic));

        Assert.Equal(0, QuestionsOf(alice));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static GeneratedItem Item(Guid documentId) =>
        new(Guid.NewGuid(), ItemKind.Question, documentId, Guid.NewGuid(), "simulated", "questions-1",
            new GeneratedQuestion($"Frage {Guid.NewGuid():N}", ["Option A", "Option B"], 1, "Erklärung", 2));

    private async Task<Question> AddQuestionAsync(string owner, Guid topicId, Guid documentId)
    {
        var question = Question.Create(
            Guid.NewGuid(), owner, topicId, documentId, 0, Guid.NewGuid(), "Was ist 2 + 2?", ["3", "4"], 1, "Zwei und zwei ist vier.", 1,
            "simulated", "questions-1", Seed.Now);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        db.Questions.Add(question);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return question;
    }

    private async Task AnswerAsync(Question question)
    {
        var attempt = Attempt.Ask(question, question.TopicId!.Value, Seed.Now);
        attempt.Answer(question, 1, 1000, Seed.Now.AddSeconds(5));
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task DeliverAsync<T>(T integrationEvent)
    {
        var messageId = await PublishAsync(integrationEvent);
        await WaitForAsync(() => Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId)) == 1, $"The {typeof(T).Name} was not handled.");
    }

    // Like Content's dispatcher would.
    private async Task<Guid> PublishAsync<T>(T integrationEvent, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        var payload = JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(T)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private int QuestionsOf(string user) => Query(db => db.Questions.IgnoreQueryFilters().Count(question => question.OwnerId == user));

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