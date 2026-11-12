using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// User A's questions stay invisible to user B, because the owner filter of ServiceDbContext limits every query to the
// current user. Every endpoint with a user's token is listed here (IsolationCheck, #271): the routes of a topic and those of
// an attempt each run with a resource of their own. The routes that take A's topic in the body have their own test.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    internal static IsolationCheck Check() => Attempts(Topics(new IsolationCheck()));

    private static IsolationCheck Topics(IsolationCheck check) => check
        .Exempt("GET", "/me", "Only the caller's own id from the token.")
        .Item("GET", "/question-pools/{topicId:guid}")
        .List("GET", "/question-pools")
        .Exempt("POST", "/attempts", "A's topic in the body: OtherUser_CannotUseAForeignTopicInABody.")
        .Exempt("POST", "/check-questions", "A's topic in the body: OtherUser_CannotUseAForeignTopicInABody.")
        .Exempt("POST", "/quizzes", "A's topic in the body: OtherUser_CannotUseAForeignTopicInABody.")
        .Exempt("POST", "/quizzes/weaknesses", "Topics come from LearningEngine for the caller: WeaknessQuizTests.ForeignWeaknessTopics_GiveNoQuestions.");

    private static IsolationCheck Attempts(IsolationCheck check) => check
        .Item("POST", "/attempts/{id:guid}/answer", new { showSolution = true, responseTimeMs = 1000 });

    [Fact]
    public void EveryEndpoint_IsInTheCheck() => Check().AssertCovers(service.Services);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorUseTheTopics() =>
        Topics(new IsolationCheck()).RunAsync(service, async owner => (await SeedAsync(owner)).Topic);

    [Fact]
    public Task OtherUser_CannotAnswerAForeignAttempt() =>
        Attempts(new IsolationCheck()).RunAsync(service, async owner => (await SeedAsync(owner)).Attempt);

    // B gets for A's topic exactly what B gets for a topic that does not exist, on every route that takes one in its body.
    [Fact]
    public async Task OtherUser_CannotUseAForeignTopicInABody()
    {
        var (topic, _) = await SeedAsync(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));
        var unknown = Guid.NewGuid();

        foreach (var (path, body) in new (string, Func<Guid, object>)[]
        {
            ("/attempts", id => new { topicId = id }),
            ("/check-questions", id => new { topicId = id, sessionId = (Guid?)null }),
            ("/quizzes", id => new { topicId = id, blockId = Guid.NewGuid(), size = 2 }),
        })
        {
            var foreign = await PostAsync(bob, path, body(topic.Id));
            var missing = await PostAsync(bob, path, body(unknown));

            Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
            Assert.Equal(missing, foreign);
        }
    }

    [Fact]
    public async Task OtherUser_DoesNotSeeTheQuestions()
    {
        var alice = NewUser("alice");
        var question = await service.AddQuestionAsync(alice);

        Assert.Equal([question.Id], await IdsSeenByAsync(alice));
        Assert.Empty(await IdsSeenByAsync(NewUser("bob")));
    }

    [Fact]
    public async Task OtherUser_DoesNotFindAForeignQuestion()
    {
        var alice = NewUser("alice");
        var question = await service.AddQuestionAsync(alice);

        var found = await AsUserAsync(NewUser("bob"), db => db.Questions.FindAsync([question.Id], TestContext.Current.CancellationToken).AsTask());
        Assert.Null(found);
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheQuestions()
    {
        var id = Guid.NewGuid().ToString("N");
        var question = await service.AddQuestionAsync($"alice-{id}");

        Assert.Equal([question.Id], await IdsSeenByAsync($"alice-{id}"));
        Assert.Empty(await IdsSeenByAsync($"Alice-{id}"));
    }

    // A topic of A with a question, and an attempt of A at it. A reads the topic's pool, the attempt has no read of its own.
    private async Task<(OwnedResource Topic, OwnedResource Attempt)> SeedAsync(string owner)
    {
        var question = await service.AddQuestionAsync(owner);
        var topic = question.TopicId!.Value;
        await service.AddKnownTopicAsync(owner, topic);
        var attempt = await service.AddAttemptAsync(question);
        var read = $"/question-pools/{topic}";
        return (new OwnedResource(topic, read, topic.ToString()), new OwnedResource(attempt.Id, read, attempt.Id.ToString()));
    }

    // The answer without the trace, which differs on every request.
    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (text.Length > 0 && JsonNode.Parse(text) is JsonObject problem)
        {
            problem.Remove("traceId");
            text = problem.ToJsonString();
        }

        return (response.StatusCode, text);
    }

    private HttpClient ClientFor(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    // Every test has its own users, so the tests of this class can share one database.
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private Task<Guid[]> IdsSeenByAsync(string user) =>
        AsUserAsync(user, db => db.Questions.Select(question => question.Id).ToArrayAsync(TestContext.Current.CancellationToken));

    // The options hold on to their scope, so the scope lives as long as the context.
    private async Task<T> AsUserAsync<T>(string user, Func<AssessmentDbContext, Task<T>> query)
    {
        using var scope = service.Services.CreateScope();
        await using var db = new AssessmentDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AssessmentDbContext>>(), new FixedUser(user));
        return await query(db);
    }

    private sealed class FixedUser(string id) : ICurrentUser
    {
        public string Id => id;
    }
}