using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// A41 through the query (#362): nothing stores the mastery, every request works it out again from the learning records
// with the rule of this build. The clock stands still, so "the same time" really is the same.
public sealed class RecomputeTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task SameRecords_SameTime_GiveTheSameAnswer_AcrossRequestsAndARestart()
    {
        var time = new TestTime();
        var alice = $"alice-{Guid.NewGuid():N}";
        var goal = Guid.NewGuid();
        var topic = Guid.NewGuid();
        var (introduction, records) = await SeedAsync(alice, goal, topic, time);

        var (onePath, listPath) = ($"/mastery/{topic}", $"/mastery?learningGoalId={goal}");

        string one, list, oneAgain, listAgain;
        IServiceProvider firstHost;
        await using (var first = Started(time))
        {
            firstHost = first.Services;
            (one, list) = (await GetAsync(first, alice, onePath), await GetAsync(first, alice, listPath));
            Assert.Equal(one, await GetAsync(first, alice, onePath));
            Assert.Equal(list, await GetAsync(first, alice, listPath));
        }

        // A new host on the same database, the first one is gone: nothing it could have kept in memory survives.
        await using (var restarted = Started(time))
        {
            Assert.NotSame(firstHost, restarted.Services);
            (oneAgain, listAgain) = (await GetAsync(restarted, alice, onePath), await GetAsync(restarted, alice, listPath));
        }

        Assert.Equal(one, oneAgain);
        Assert.Equal(list, listAgain);

        // Value and version belong together: what the current rule makes of these records, named by its version.
        var expected = TopicStates.Assess(records, introduction, time.GetUtcNow().UtcDateTime);
        var state = expected.State.ToString().ToLowerInvariant();
        Assert.Equal(new OneTopic(topic, expected.Mastery, state, Mastery.RuleVersion), Read<OneTopic>(one));
        var read = Read<MasteryList>(list);
        Assert.Equal(Mastery.RuleVersion, read.RuleVersion);
        Assert.Equal([new Entry(topic, expected.Mastery, state)], read.Topics);
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)!;

    private WebApplicationFactory<Program> Started(TimeProvider time) =>
        service.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(time)));

    // Introduced eight weeks ago, four weeks of answers, then four weeks without review. Whole seconds, so the values
    // read back from the database are exactly the ones seeded.
    private async Task<(TopicIntroduction, List<LearningRecord>)> SeedAsync(string owner, Guid goal, Guid topic, TestTime time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var introduced = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc).AddDays(-56);
        var introduction = TopicIntroduction.Create(owner, topic, Guid.NewGuid(), introduced);
        List<LearningRecord> records =
        [
            .. new (bool Correct, int Difficulty, int Day)[]
            {
                (true, 1, 0), (false, 2, 3), (true, 3, 7), (true, 2, 12), (false, 1, 16), (true, 3, 21), (true, 2, 28),
            }.Select(answer => LearningRecord.Create(
                Guid.NewGuid(), owner, topic, Guid.NewGuid(), Guid.NewGuid(), answer.Correct, answer.Difficulty, 4200, null,
                Mode.Practice, introduced.AddDays(answer.Day))),
        ];

        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.Add(KnownTopic.Create(owner, topic, goal, hidden: false));
        db.TopicIntroductions.Add(introduction);
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (introduction, records);
    }

    private static async Task<string> GetAsync(WebApplicationFactory<Program> host, string user, string path)
    {
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private sealed record OneTopic(Guid TopicId, int Mastery, string State, int RuleVersion);

    private sealed record MasteryList(int RuleVersion, IReadOnlyList<Entry> Topics);

    private sealed record Entry(Guid TopicId, int Mastery, string State);
}