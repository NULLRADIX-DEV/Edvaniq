using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// GET /question-pools: how many usable questions each of the user's topics has and whether its pool is ready, only for
// the user of the token.
public sealed class QuestionPoolTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task EachTopic_HasItsCountAndStatus()
    {
        var alice = User.New("alice");
        var goal = Guid.NewGuid();
        var none = await AddTopicAsync(alice, goal, questions: 0);
        var one = await AddTopicAsync(alice, goal, questions: 1);
        var two = await AddTopicAsync(alice, goal, questions: 2);
        var many = await AddTopicAsync(alice, goal, questions: 17);

        var pools = await GetAsync(alice, $"/question-pools?learningGoalId={goal}");

        Assert.Equal(QuestionPool.MinQuestions, pools.GetProperty("minQuestions").GetInt32());
        Assert.Equal(
            new[] { (none, 0, "notReady"), (one, 1, "notReady"), (two, 2, "ready"), (many, 17, "ready") }.OrderBy(entry => entry.Item1),
            Entries(pools));
    }

    [Fact]
    public async Task OneTopic_HasItsCountAndStatus()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, Guid.NewGuid(), questions: 1);

        var pool = await GetAsync(alice, $"/question-pools/{topic}");

        Assert.Equal(
            (topic, 1, "notReady", QuestionPool.MinQuestions),
            (pool.GetProperty("topicId").GetGuid(), pool.GetProperty("questions").GetInt32(), pool.GetProperty("status").GetString(),
                pool.GetProperty("minQuestions").GetInt32()));
    }

    // Questions taken over before topic ids came are never asked, so they don't count.
    [Fact]
    public async Task QuestionsWithoutATopicId_DontCount()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, Guid.NewGuid(), questions: 1);
        await service.AddQuestionAsync(alice.Id);
        await AddLegacyQuestionAsync(alice);

        var pool = await GetAsync(alice, $"/question-pools/{topic}");

        Assert.Equal((1, "notReady"), (pool.GetProperty("questions").GetInt32(), pool.GetProperty("status").GetString()));
    }

    // Bob sees nothing of Alice's topic, and Alice counts only her own questions, even with Bob's under the same topic id.
    [Fact]
    public async Task SomeoneElsesTopic_GivesNoData()
    {
        var alice = User.New("alice");
        var bob = User.New("bob");
        var goal = Guid.NewGuid();
        var topic = await AddTopicAsync(alice, goal, questions: 1);
        await AddQuestionsAsync(bob, topic, 3);

        using var client = ClientFor(bob);
        using var one = await client.GetAsync($"/question-pools/{topic}", TestContext.Current.CancellationToken);
        var bobs = await GetAsync(bob, $"/question-pools?learningGoalId={goal}");
        var all = await GetAsync(bob, "/question-pools");
        var alices = await GetAsync(alice, $"/question-pools/{topic}");

        Assert.Equal(HttpStatusCode.NotFound, one.StatusCode);
        Assert.Empty(bobs.GetProperty("topics").EnumerateArray());
        Assert.Empty(all.GetProperty("topics").EnumerateArray());
        Assert.Equal((1, "notReady"), (alices.GetProperty("questions").GetInt32(), alices.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task WithoutAGoal_AllTopicsCome_WithAGoal_OnlyItsOwn()
    {
        var alice = User.New("alice");
        var first = Guid.NewGuid();
        var a = await AddTopicAsync(alice, first, questions: 2);
        var b = await AddTopicAsync(alice, Guid.NewGuid(), questions: 0);

        var all = await GetAsync(alice, "/question-pools");
        var ofFirst = await GetAsync(alice, $"/question-pools?learningGoalId={first}");

        Assert.Equal(new[] { (a, 2, "ready"), (b, 0, "notReady") }.OrderBy(entry => entry.Item1), Entries(all));
        Assert.Equal([(a, 2, "ready")], Entries(ofFirst));
    }

    [Fact]
    public async Task UnknownTopic_IsNotFound()
    {
        using var client = ClientFor(User.New("alice"));

        using var response = await client.GetAsync($"/question-pools/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BrokenGoalId_IsRejected()
    {
        using var client = ClientFor(User.New("alice"));

        using var response = await client.GetAsync("/question-pools?learningGoalId=nope", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty("learningGoalId", out _));
    }

    [Fact]
    public async Task WithoutAToken_IsUnauthorized()
    {
        using var client = service.CreateClient();

        using var response = await client.GetAsync("/question-pools", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record User(string Id, string Token)
    {
        public static User New(string name)
        {
            var id = $"{name}-{Guid.NewGuid():N}";
            return new(id, TestTokens.Create(id));
        }
    }

    private static List<(Guid, int, string)> Entries(JsonElement pools) =>
        [.. pools.GetProperty("topics").EnumerateArray().Select(topic => (
            topic.GetProperty("topicId").GetGuid(), topic.GetProperty("questions").GetInt32(), topic.GetProperty("status").GetString()!))];

    // A topic of the user known from TopicsChanged, in this learning goal, with that many questions.
    private async Task<Guid> AddTopicAsync(User user, Guid learningGoalId, int questions)
    {
        var topicId = Guid.NewGuid();
        using (var scope = service.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
            db.KnownTopics.Add(KnownTopic.Create(user.Id, topicId, learningGoalId, hidden: false));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await AddQuestionsAsync(user, topicId, questions);
        return topicId;
    }

    private async Task AddQuestionsAsync(User user, Guid topicId, int count)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.Questions.Add(Question.Create(
                Guid.NewGuid(), user.Id, topicId, Guid.NewGuid(), 0, Guid.NewGuid(), "Was ist 2 + 2?", ["3", "4"], 1, "Zwei und zwei ist vier.",
                1, "simulated", "questions-1", Seed.Now));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // A question as taken over before topic ids came: no TopicId.
    private async Task AddLegacyQuestionAsync(User user)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        var question = Seed.Question(user.Id);
        db.Questions.Add(question);
        db.Entry(question).Property(nameof(Question.TopicId)).CurrentValue = null;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private HttpClient ClientFor(User user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client;
    }

    private async Task<JsonElement> GetAsync(User user, string path)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}