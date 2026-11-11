using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// The check questions of the learning unit "Neues Thema": two or three of the topic's questions, the easy ones first,
// only the user's, and a status instead of an error while the pool isn't there.
public sealed class CheckQuestionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly string Subject = EventRoute.For(typeof(AnswerRecorded)).Subject;

    [Fact]
    public async Task FromAMixedPool_ThreeComeEasiestFirst_WithoutSolution()
    {
        var alice = User.New("alice");
        var topicId = await AddTopicAsync(alice, 3, 2, 1, 3, 1, 2);

        var check = await CheckAsync(alice, topicId);

        Assert.Equal((topicId, "ready"), (check.GetProperty("topicId").GetGuid(), check.GetProperty("status").GetString()));
        var questions = check.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal([1, 1, 2], questions.Select(question => question.GetProperty("difficulty").GetInt32()));
        Assert.Equal(3, questions.Select(question => question.GetProperty("questionId").GetGuid()).Distinct().Count());
        Assert.All(questions, question => Assert.Equal(
            ["difficulty", "id", "options", "questionId", "text", "topicId", "type"],
            question.EnumerateObject().Select(property => property.Name).Order()));
        Assert.All(questions, question => Assert.False(Stored(question.GetProperty("id").GetGuid()).Answered));
    }

    [Fact]
    public async Task ExactlyTwoQuestions_BothCome()
    {
        var alice = User.New("alice");
        var topicId = await AddTopicAsync(alice, 2, 1);

        var check = await CheckAsync(alice, topicId);

        Assert.Equal("ready", check.GetProperty("status").GetString());
        Assert.Equal([1, 2], check.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("difficulty").GetInt32()));
    }

    // Each check question is answered like any attempt: once, with exactly one learning record.
    [Fact]
    public async Task EachAnswer_IsOneLearningRecord()
    {
        var alice = User.New("alice");
        var topicId = await AddTopicAsync(alice, 1, 1, 1);
        var ids = (await CheckAsync(alice, topicId)).GetProperty("questions").EnumerateArray()
            .Select(question => question.GetProperty("id").GetGuid())
            .ToList();
        using var client = ClientFor(alice);

        foreach (var (id, body) in ids.Zip(new object[] { new { option = 1, responseTimeMs = 0 }, new { option = 0, responseTimeMs = 0 }, new { showSolution = true, responseTimeMs = 0 } }))
        {
            using var first = await client.PostAsJsonAsync($"/attempts/{id}/answer", body, TestContext.Current.CancellationToken);
            using var again = await client.PostAsJsonAsync($"/attempts/{id}/answer", body, TestContext.Current.CancellationToken);
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, again.StatusCode));
        }

        Assert.Equal([true, false, false], ids.Select(id => Assert.Single(Records(id)).Correct));
    }

    // The session of the unit goes with each answer, so LearningEngine counts the check questions once it introduced
    // the topic. Without one, the answer has none, like a quiz answer.
    [Fact]
    public async Task WithASession_EachAnswerCarriesIt()
    {
        var alice = User.New("alice");
        var topicId = await AddTopicAsync(alice, 1, 1, 1);
        var session = Guid.NewGuid();

        var inSession = await AnswerAllAsync(alice, await CheckAsync(alice, topicId, session));
        var without = await AnswerAllAsync(alice, await CheckAsync(alice, topicId));

        Assert.All(inSession, id => Assert.Equal(session, Stored(id).SessionId));
        Assert.All(inSession, id => Assert.Equal(session, Assert.Single(Records(id)).SessionId));
        Assert.All(without, id => Assert.Null(Assert.Single(Records(id)).SessionId));
    }

    // Assessment can't tell whose session an id is. Bob passing Alice's still only gives learning records of Bob's own,
    // on his topic, and leaves Alice's alone.
    [Fact]
    public async Task SomeoneElsesSession_OnlyGoesWithTheUsersOwnAnswers()
    {
        var (alice, bob) = (User.New("alice"), User.New("bob"));
        var aliceTopic = await AddTopicAsync(alice, 1, 1);
        var bobTopic = await AddTopicAsync(bob, 1, 1);
        var session = Guid.NewGuid();
        var aliceAnswers = await AnswerAllAsync(alice, await CheckAsync(alice, aliceTopic, session));

        var bobAnswers = await AnswerAllAsync(bob, await CheckAsync(bob, bobTopic, session));

        Assert.All(bobAnswers, id => Assert.Equal((bob.Id, bobTopic, session), Single(id)));
        Assert.All(aliceAnswers, id => Assert.Equal((alice.Id, aliceTopic, session), Single(id)));
        Assert.Equal(2, Records().Count(record => record.UserId == alice.Id));

        (string, Guid, Guid?) Single(Guid id)
        {
            var record = Assert.Single(Records(id));
            return (record.UserId, record.TopicId, record.SessionId);
        }
    }

    // The questions come some time after the topic: a known topic without enough of them says so, nothing is asked.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PoolNotThereYet_IsAStatus_NotAnError(int count)
    {
        var alice = User.New("alice");
        var topicId = await AddTopicAsync(alice, [.. Enumerable.Repeat(1, count)]);
        await service.AddQuestionAsync(alice.Id);

        var check = await CheckAsync(alice, topicId);

        Assert.Equal("notReady", check.GetProperty("status").GetString());
        Assert.Empty(check.GetProperty("questions").EnumerateArray());
        Assert.Equal(count, Query(db => db.Questions.IgnoreQueryFilters().Count(question => question.OwnerId == alice.Id && question.TopicId == topicId)));
        Assert.Equal(0, Query(db => db.Attempts.IgnoreQueryFilters().Count(attempt => attempt.OwnerId == alice.Id)));
    }

    // Bob gets nothing of Alice's topic, and Alice only her own questions, even with Bob's under the same topic id.
    [Fact]
    public async Task OnlyTheUsersQuestions()
    {
        var alice = User.New("alice");
        var bob = User.New("bob");
        var topicId = await AddTopicAsync(alice, 2, 2, 2);
        await AddQuestionsAsync(bob, topicId, 1, 1, 1);

        using var client = ClientFor(bob);
        using var foreign = await client.PostAsJsonAsync("/check-questions", new { topicId }, TestContext.Current.CancellationToken);
        var check = await CheckAsync(alice, topicId);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var asked = check.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("questionId").GetGuid()).ToList();
        Assert.Equal(3, asked.Count);
        Assert.All(asked, id => Assert.Equal(alice.Id, Query(db => db.Questions.IgnoreQueryFilters().Single(question => question.Id == id).OwnerId)));
    }

    [Fact]
    public async Task UnknownTopic_IsNotFound()
    {
        using var client = ClientFor(User.New("alice"));

        using var response = await client.PostAsJsonAsync("/check-questions", new { topicId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WithoutATopic_IsRejected()
    {
        using var client = ClientFor(User.New("alice"));

        using var response = await client.PostAsJsonAsync("/check-questions", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record User(string Id, string Token)
    {
        public static User New(string name)
        {
            var id = $"{name}-{Guid.NewGuid():N}";
            return new(id, TestTokens.Create(id));
        }
    }

    // A topic of the user known from TopicsChanged, with a question of each given difficulty.
    private async Task<Guid> AddTopicAsync(User user, params int[] difficulties)
    {
        var topicId = Guid.NewGuid();
        await service.AddKnownTopicAsync(user.Id, topicId);
        await AddQuestionsAsync(user, topicId, difficulties);
        return topicId;
    }

    private async Task AddQuestionsAsync(User user, Guid topicId, params int[] difficulties)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        foreach (var difficulty in difficulties)
        {
            db.Questions.Add(Question.Create(
                Guid.NewGuid(), user.Id, topicId, Guid.NewGuid(), 0, Guid.NewGuid(), "Was ist 2 + 2?", ["3", "4"], 1, "Zwei und zwei ist vier.",
                difficulty, "simulated", "questions-1", Seed.Now));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private HttpClient ClientFor(User user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client;
    }

    private async Task<JsonElement> CheckAsync(User user, Guid topicId, Guid? sessionId = null)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync("/check-questions", new { topicId, sessionId }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<List<Guid>> AnswerAllAsync(User user, JsonElement check)
    {
        var ids = check.GetProperty("questions").EnumerateArray().Select(question => question.GetProperty("id").GetGuid()).ToList();
        using var client = ClientFor(user);
        foreach (var id in ids)
        {
            using var response = await client.PostAsJsonAsync($"/attempts/{id}/answer", new { option = 1, responseTimeMs = 0 }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return ids;
    }

    private Attempt Stored(Guid attemptId) =>
        Query(db => db.Attempts.IgnoreQueryFilters().AsNoTracking().Single(attempt => attempt.Id == attemptId));

    private List<AnswerRecorded> Records(Guid answerId) => [.. Records().Where(record => record.AnswerId == answerId)];

    private List<AnswerRecorded> Records() =>
        [.. Query(db => db.Set<OutboxMessage>().AsNoTracking().Where(message => message.Subject == Subject).Select(message => message.Payload).ToList())
            .Select(payload => JsonSerializer.Deserialize<AnswerRecorded>(payload, JsonSerializerOptions.Web)!)];

    private T Query<T>(Func<AssessmentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AssessmentDbContext>());
    }
}