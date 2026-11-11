using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// GET /mastery/decayed: the topics of one learning goal that were secure at their last answer and have decayed since.
public sealed class DecayedMasteryTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    // AK1 and AK3: of four topics only those secure at their last answer and below SecureFrom now, most decayed first.
    [Fact]
    public async Task ListsOnlyTopicsDecayedBelowSecure_MostDecayedFirst()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (month, twoMonths, fresh, building) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(
            [Topic(alice, month, goal), Topic(alice, twoMonths, goal), Topic(alice, fresh, goal), Topic(alice, building, goal)],
            [
                .. SecureAnswers(alice, month, Now.AddDays(-30)),
                .. SecureAnswers(alice, twoMonths, Now.AddDays(-60)),
                .. SecureAnswers(alice, fresh, Now.AddMinutes(-10)),
                Record(alice, building, Now.AddDays(-30)),
            ]);

        var list = await DecayedAsync(alice, goal);

        Assert.Equal(Mastery.RuleVersion, list.RuleVersion);
        Assert.Equal(TopicStates.SecureFrom, list.Threshold);
        Assert.Equal([twoMonths, month], list.Topics.Select(topic => topic.TopicId));
        Assert.All(list.Topics, topic =>
        {
            Assert.True(topic.Mastery < TopicStates.SecureFrom);
            Assert.True(topic.MasteryAtLastAnswer >= TopicStates.SecureFrom);
        });
        Assert.Equal(Now.AddDays(-30).AddMinutes(3), list.Topics[1].LastAnsweredAt, TimeSpan.FromMilliseconds(1));
    }

    // AK2: per learning goal of the user, and only the user's own.
    [Fact]
    public async Task OnlyTheAskedGoal_OfTheUserFromTheToken()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var (biology, history) = (Guid.NewGuid(), Guid.NewGuid());
        var (cell, rome) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(
            [Topic(alice, cell, biology), Topic(alice, rome, history)],
            [.. SecureAnswers(alice, cell, Now.AddDays(-30)), .. SecureAnswers(alice, rome, Now.AddDays(-30))]);

        Assert.Equal([cell], (await DecayedAsync(alice, biology)).Topics.Select(topic => topic.TopicId));
        Assert.Equal([rome], (await DecayedAsync(alice, history)).Topics.Select(topic => topic.TopicId));
        Assert.Empty((await DecayedAsync(alice, Guid.NewGuid())).Topics);
        Assert.Empty((await DecayedAsync(bob, biology)).Topics);
    }

    [Theory]
    [InlineData("", "Is required.")]
    [InlineData("?learningGoalId=not-a-guid", "Must be a GUID.")]
    public async Task MissingOrBrokenLearningGoalId_IsAProblem(string query, string message)
    {
        using var response = await GetAsync(NewUser("alice"), query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(message, problem.GetProperty("errors").GetProperty("learningGoalId")[0].GetString());
    }

    [Fact]
    public async Task Request_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.GetAsync($"/mastery/decayed?learningGoalId={Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static KnownTopic Topic(string owner, Guid topicId, Guid learningGoalId) =>
        KnownTopic.Create(owner, topicId, learningGoalId, hidden: false);

    // Four right medium answers a minute apart give 84, secure at the last one.
    private static IEnumerable<LearningRecord> SecureAnswers(string owner, Guid topicId, DateTime from) =>
        Enumerable.Range(0, 4).Select(minute => Record(owner, topicId, from.AddMinutes(minute)));

    private static LearningRecord Record(string owner, Guid topicId, DateTime answeredAt) =>
        LearningRecord.Create(Guid.NewGuid(), owner, topicId, Guid.NewGuid(), null, true, 2, 4200, null, Mode.Practice, answeredAt);

    private async Task SeedAsync(IEnumerable<KnownTopic> topics, IEnumerable<LearningRecord> records)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.AddRange(topics);
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<DecayedList> DecayedAsync(string user, Guid learningGoalId)
    {
        using var response = await GetAsync(user, $"?learningGoalId={learningGoalId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DecayedList>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string query)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mastery/decayed" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record DecayedList(int RuleVersion, int Threshold, IReadOnlyList<Entry> Topics);

    private sealed record Entry(Guid TopicId, int Mastery, int MasteryAtLastAnswer, DateTime LastAnsweredAt);
}