using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// #389: a topic the user hid asks no question in any quiz, shown again it asks the same ones as before. A topic that
// left the learning goal takes its questions and their attempts along, but only with a newer state of the goal.
// Knowledge names the subtopics of a hidden topic as hidden too (TopicsChangedEvents), so they need nothing here.
public sealed class HiddenTopicTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-TopicsChangedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly DateTime First = new(2026, 11, 15, 9, 0, 0, 123, 456, DateTimeKind.Utc);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task HiddenTopic_AsksNothingAnywhere_ShownAgain_AsksItsQuestionsAgain()
    {
        var alice = User.New("alice");
        var goal = Guid.NewGuid();
        var (topic, other) = (Guid.NewGuid(), Guid.NewGuid());
        await DeliverAsync(new TopicsChanged(alice.Id, goal, First, [topic, other], []));
        var questions = await AddQuestionsAsync(alice, topic);
        await AddQuestionsAsync(alice, other);
        var block = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(alice, "/quizzes", new { topicId = topic, blockId = block, size = 3 })).Status);

        await DeliverAsync(new TopicsChanged(alice.Id, goal, First.AddSeconds(1), [other], [topic]));

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(alice, "/attempts", new { topicId = topic })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(alice, "/check-questions", new { topicId = topic })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(alice, "/quizzes", new { topicId = topic, blockId = Guid.NewGuid(), size = 3 })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(alice, "/quizzes", new { topicId = topic, blockId = block, size = 3 })).Status);
        service.LearningEngine.Weaknesses(alice.Token, topic, other);
        var (status, weaknesses) = await PostAsync(alice, "/quizzes/weaknesses", new { learningGoalId = goal, size = 4 });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.All(weaknesses!.Value.GetProperty("questions").EnumerateArray(), question =>
            Assert.Equal(other, question.GetProperty("topicId").GetGuid()));
        Assert.Equal(questions.Count, Query(db => db.Questions.IgnoreQueryFilters().Count(question => question.TopicId == topic)));

        await DeliverAsync(new TopicsChanged(alice.Id, goal, First.AddSeconds(2), [topic, other], []));

        var (asked, attempt) = await PostAsync(alice, "/attempts", new { topicId = topic });
        Assert.Equal(HttpStatusCode.Created, asked);
        Assert.Contains(attempt!.Value.GetProperty("questionId").GetGuid(), questions);
    }

    [Fact]
    public async Task TopicLeavingTheGoal_TakesItsQuestionsAndAttempts_OnlyWithANewerState()
    {
        var alice = User.New("alice");
        var bob = User.New("bob");
        var goal = Guid.NewGuid();
        var (gone, kept, bobs) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await DeliverAsync(new TopicsChanged(alice.Id, goal, First, [gone, kept], []));
        await DeliverAsync(new TopicsChanged(bob.Id, goal, First, [bobs], []));
        var goneQuestions = await AddQuestionsAsync(alice, gone);
        var keptQuestions = await AddQuestionsAsync(alice, kept);
        var bobsQuestions = await AddQuestionsAsync(bob, bobs);
        var (_, attempt) = await PostAsync(alice, "/attempts", new { topicId = gone });
        var attemptId = attempt!.Value.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(alice, $"/attempts/{attemptId}/answer", new { option = 1, responseTimeMs = 0 })).Status);

        // The same time and an older one: no newer state, nothing goes.
        await DeliverAsync(new TopicsChanged(alice.Id, goal, First, [kept], []));
        await DeliverAsync(new TopicsChanged(alice.Id, goal, First.AddSeconds(-1), [kept], []));
        Assert.Equal(goneQuestions.Count, QuestionsOf(gone));
        Assert.Equal(1, AttemptsOn(goneQuestions));

        await DeliverAsync(new TopicsChanged(alice.Id, goal, First.AddSeconds(1), [kept], []));

        Assert.Equal(0, QuestionsOf(gone));
        Assert.Equal(0, AttemptsOn(goneQuestions));
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(alice, "/attempts", new { topicId = gone })).Status);
        Assert.Equal(keptQuestions.Count, QuestionsOf(kept));
        Assert.Equal(bobsQuestions.Count, QuestionsOf(bobs));
    }

    // Single choice questions of a known topic, the right option is 1.
    private async Task<List<Guid>> AddQuestionsAsync(User user, Guid topicId)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            db.Questions.Add(Question.Create(
                id, user.Id, topicId, Guid.NewGuid(), 0, Guid.NewGuid(), $"Frage {index}", ["3", "4"], 1, "Zwei und zwei ist vier.", 1,
                "simulated", "questions-1", Seed.Now));
        }

        await db.SaveChangesAsync(Cancel);
        return ids;
    }

    private int QuestionsOf(Guid topicId) =>
        Query(db => db.Questions.IgnoreQueryFilters().Count(question => question.TopicId == topicId));

    private int AttemptsOn(List<Guid> questionIds) =>
        Query(db => db.Attempts.IgnoreQueryFilters().Count(attempt => questionIds.Contains(attempt.QuestionId)));

    private async Task<(HttpStatusCode Status, JsonElement? Body)> PostAsync(User user, string path, object body)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        using var response = await client.PostAsJsonAsync(path, body, Cancel);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(Cancel))
            : (response.StatusCode, null);
    }

    private async Task DeliverAsync(TopicsChanged changed)
    {
        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(changed, JsonSerializerOptions.Web);
        await Broker.PublishAsync(new OutgoingMessage(EventRoute.For(typeof(TopicsChanged)).Subject, messageId, payload, null), Cancel);
        var deadline = DateTime.UtcNow + Timeout;
        while (Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer)) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The state was not handled.");
            await Task.Delay(100, Cancel);
        }
    }

    private T Query<T>(Func<AssessmentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AssessmentDbContext>());
    }

    private sealed record User(string Id, string Token)
    {
        public static User New(string name)
        {
            var id = $"{name}-{Guid.NewGuid():N}";
            return new(id, TestTokens.Create(id));
        }
    }
}