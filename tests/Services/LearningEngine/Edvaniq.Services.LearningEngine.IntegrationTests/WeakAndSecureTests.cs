using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// GET /mastery/weak-and-secure (#453): the weaknesses the worst first and the secure topics, only of the user from the
// token.
public sealed class WeakAndSecureTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    // AC1: two weaknesses by mastery, the lower first, then one only by its last answers; secure ones the highest first;
    // building and new ones in neither list.
    [Fact]
    public async Task Weaknesses_TheWorstFirst_SecureTheHighestFirst()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (lowest, low, errors, secure, mostSecure, building, fresh) =
            (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(
            [.. new[] { lowest, low, errors, secure, mostSecure, building, fresh }.Select(topic => Topic(alice, topic, goal))],
            [
                .. Answers(alice, lowest, (false, 2), (false, 2), (false, 2), (false, 2)),
                .. Answers(alice, low, (false, 2), (false, 2)),
                .. Answers(alice, errors, (true, 3), (true, 3), (true, 3), (true, 3), (false, 2), (false, 2)),
                .. Answers(alice, secure, (true, 2), (true, 2), (true, 2), (true, 2)),
                .. Answers(alice, mostSecure, (true, 3), (true, 3), (true, 3), (true, 3)),
                .. Answers(alice, building, (true, 2)),
            ]);

        var lists = await ListsAsync(alice, $"?learningGoalId={goal}");

        Assert.Equal(Mastery.RuleVersion, lists.RuleVersion);
        Assert.Equal([lowest, low, errors], lists.Weaknesses.Select(topic => topic.TopicId));
        Assert.Equal(["mastery", "mastery", "recentErrors"], lists.Weaknesses.Select(topic => topic.Reason));
        Assert.True(lists.Weaknesses[2].Mastery >= TopicStates.WeakBelow);
        Assert.Equal([mostSecure, secure], lists.Secure.Select(topic => topic.TopicId));
        Assert.All(lists.Secure, topic => Assert.True(topic.Mastery >= TopicStates.SecureFrom));
        Assert.Equal(lists, await ListsAsync(alice, ""));
    }

    // AC2: Bob sees only his own topics, Alice's goal gives him two empty lists, and Alice doesn't see his.
    [Fact]
    public async Task EachUser_SeesOnlyTheirOwnTopics()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var goal = Guid.NewGuid();
        var (hers, his) = (Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(
            [Topic(alice, hers, goal), Topic(bob, his, goal)],
            [.. Answers(alice, hers, (false, 2), (false, 2)), .. Answers(bob, his, (true, 3), (true, 3), (true, 3), (true, 3))]);

        var ofAlice = await ListsAsync(alice, "");
        var ofBob = await ListsAsync(bob, $"?learningGoalId={goal}");

        Assert.Equal([hers], ofAlice.Weaknesses.Select(topic => topic.TopicId));
        Assert.Empty(ofAlice.Secure);
        Assert.Empty(ofBob.Weaknesses);
        Assert.Equal([his], ofBob.Secure.Select(topic => topic.TopicId));
        var otherGoal = await ListsAsync(alice, $"?learningGoalId={Guid.NewGuid()}");
        Assert.Empty(otherGoal.Weaknesses);
        Assert.Empty(otherGoal.Secure);
    }

    [Fact]
    public async Task Request_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.GetAsync("/mastery/weak-and-secure", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BrokenLearningGoalId_IsAProblem()
    {
        using var response = await GetAsync(NewUser("alice"), "?learningGoalId=not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static KnownTopic Topic(string owner, Guid topicId, Guid learningGoalId) =>
        KnownTopic.Create(owner, topicId, learningGoalId, hidden: false);

    // One answer a minute, ending a few minutes ago.
    private static IEnumerable<LearningRecord> Answers(string owner, Guid topicId, params (bool Correct, int Difficulty)[] answers) =>
        answers.Select((answer, index) => LearningRecord.Create(
            Guid.NewGuid(), owner, topicId, Guid.NewGuid(), null, answer.Correct, answer.Difficulty, 4200, null, Mode.Practice,
            Now.AddMinutes(index - answers.Length - 1)));

    private async Task SeedAsync(IEnumerable<KnownTopic> topics, IEnumerable<LearningRecord> records)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.AddRange(topics);
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Lists> ListsAsync(string user, string query)
    {
        using var response = await GetAsync(user, query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Lists>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string query)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mastery/weak-and-secure" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // Compared by content, the lists included.
    private sealed record Lists(int RuleVersion, List<Weakness> Weaknesses, List<SecureEntry> Secure)
    {
        public bool Equals(Lists? other) =>
            other is not null && RuleVersion == other.RuleVersion && Weaknesses.SequenceEqual(other.Weaknesses) && Secure.SequenceEqual(other.Secure);

        public override int GetHashCode() => RuleVersion;
    }

    private sealed record Weakness(Guid TopicId, int Mastery, string Reason);

    private sealed record SecureEntry(Guid TopicId, int Mastery);
}