using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// #390: a topic Knowledge hides leaves the lists of mastery, shown again it comes back with the mastery of its answers,
// and a topic gone gives no values any more while its records stay (#401). The states arrive as TopicsChanged through the
// broker, like from Knowledge.
public sealed class HiddenTopicMasteryTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-TopicsChangedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly DateTime First = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = DateTime.UtcNow;

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    // AC1 and AC2, also for the weaknesses (Refs #455) and the decayed topics: hidden, a topic is in none of the lists of
    // its goal or of all topics, asked for by its id it still answers. Shown again, every list is as before.
    [Fact]
    public async Task HiddenTopic_LeavesTheLists_AndComesBackWithItsMastery()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (weak, decayed, secure) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await DeliverAsync(new TopicsChanged(alice, goal, First, [weak, decayed, secure], []));
        await SeedAsync(
        [
            Record(alice, weak, false, Now.AddMinutes(-3)),
            Record(alice, weak, false, Now.AddMinutes(-2)),
            .. Enumerable.Range(0, 4).Select(minute => Record(alice, decayed, true, Now.AddDays(-30).AddMinutes(minute))),
            .. Enumerable.Range(0, 4).Select(minute => Record(alice, secure, true, Now.AddMinutes(minute - 10))),
        ]);
        var before = await ListAsync(alice, $"?learningGoalId={goal}");
        Assert.Equal(new[] { weak, decayed, secure }.Order(), before.Topics.Select(topic => topic.TopicId));
        Assert.Contains(weak, (await WeakAndSecureAsync(alice, goal)).Weaknesses.Select(topic => topic.TopicId));
        Assert.Equal([decayed], (await DecayedAsync(alice, goal)).Topics.Select(topic => topic.TopicId));

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [secure], [weak, decayed]));

        Assert.Equal([secure], (await ListAsync(alice, $"?learningGoalId={goal}")).Topics.Select(topic => topic.TopicId));
        Assert.Equal([secure], (await ListAsync(alice)).Topics.Select(topic => topic.TopicId));
        var lists = await WeakAndSecureAsync(alice, goal);
        Assert.Empty(lists.Weaknesses);
        Assert.Equal([secure], lists.Secure.Select(topic => topic.TopicId));
        Assert.Empty((await WeakAndSecureAsync(alice, null)).Weaknesses);
        Assert.Empty((await DecayedAsync(alice, goal)).Topics);
        using (var single = await GetAsync(alice, $"/mastery/{weak}"))
        {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        }

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(2), [weak, decayed, secure], []));

        Assert.Equal(before, await ListAsync(alice, $"?learningGoalId={goal}"));
        Assert.Contains(weak, (await WeakAndSecureAsync(alice, goal)).Weaknesses.Select(topic => topic.TopicId));
        Assert.Equal([decayed], (await DecayedAsync(alice, goal)).Topics.Select(topic => topic.TopicId));
    }

    // AC3 and #401: a topic in neither list is gone, in no list and not even by its id. Its records stay in the
    // database, they go with their deadline or the user.
    [Fact]
    public async Task GoneTopic_GivesNoValues_ButItsRecordsStay()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (gone, kept) = (Guid.NewGuid(), Guid.NewGuid());
        await DeliverAsync(new TopicsChanged(alice, goal, First, [gone, kept], []));
        await SeedAsync([Record(alice, gone, false, Now.AddMinutes(-2)), Record(alice, kept, true, Now.AddMinutes(-1))]);

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [kept], []));

        Assert.Equal([kept], (await ListAsync(alice, $"?learningGoalId={goal}")).Topics.Select(topic => topic.TopicId));
        Assert.Equal([kept], (await ListAsync(alice)).Topics.Select(topic => topic.TopicId));
        Assert.Empty((await WeakAndSecureAsync(alice, null)).Weaknesses);
        using (var single = await GetAsync(alice, $"/mastery/{gone}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, single.StatusCode);
        }

        Assert.Equal(1, Query(db => db.LearningRecords.IgnoreQueryFilters().Count(record => record.OwnerId == alice && record.TopicId == gone)));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static LearningRecord Record(string owner, Guid topicId, bool correct, DateTime answeredAt) =>
        LearningRecord.Create(Guid.NewGuid(), owner, topicId, Guid.NewGuid(), null, correct, 2, 4200, null, Mode.Practice, answeredAt);

    private async Task SeedAsync(IEnumerable<LearningRecord> records)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task DeliverAsync(TopicsChanged changed)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(changed, JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(TopicsChanged)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow + Timeout;
        while (Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == id && entry.Consumer == Consumer)) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The state was not handled.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private Task<MasteryList> ListAsync(string user, string query = "") => ReadAsync<MasteryList>(user, "/mastery" + query);

    private Task<Lists> WeakAndSecureAsync(string user, Guid? goal) =>
        ReadAsync<Lists>(user, "/mastery/weak-and-secure" + (goal is null ? "" : $"?learningGoalId={goal}"));

    private Task<DecayedList> DecayedAsync(string user, Guid goal) => ReadAsync<DecayedList>(user, $"/mastery/decayed?learningGoalId={goal}");

    private async Task<T> ReadAsync<T>(string user, string path)
    {
        using var response = await GetAsync(user, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string path)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private T Query<T>(Func<LearningEngineDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>());
    }

    // Compared by content, the list included.
    private sealed record MasteryList(int RuleVersion, IReadOnlyList<Entry> Topics)
    {
        public bool Equals(MasteryList? other) => other is not null && RuleVersion == other.RuleVersion && Topics.SequenceEqual(other.Topics);

        public override int GetHashCode() => RuleVersion;
    }

    private sealed record Entry(Guid TopicId, int Mastery, string State, Guid? LastAnswerId);

    private sealed record Lists(List<Topic> Weaknesses, List<Topic> Secure);

    private sealed record Topic(Guid TopicId, int Mastery);

    private sealed record DecayedList(IReadOnlyList<Topic> Topics);
}