using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// The topic state in both answers of /mastery, worked out with the mastery from the same records (#364).
public sealed class TopicStateQueryTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    // A37 through the query. Every value sits at least 0.4 away from a rounding edge, so the minutes a CI run takes
    // between seeding and asking (decay about 0.0003 per minute here) can't push it over:
    // 49: one correct medium answer 31.6 days ago, 62.5 * 0.5^(31.6/90) = 49.00
    // 50: introduced, no answers, exactly the start value
    // 79: three correct medium answers minutes ago, 78.91
    // 80: four correct medium answers 6.6 days ago, 84.18 * 0.5^(6.6/90) = 80.01
    [Fact]
    public async Task IntroducedTopics_At49_50_79_80_AreWeakness_Building_Building_Secure()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (t49, t50, t79, t80) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(
            alice,
            goal,
            [t49, t50, t79, t80],
            [
                .. Correct(alice, t49, 1, Now.AddDays(-31.6)),
                .. Correct(alice, t79, 3, Now.AddMinutes(-5)),
                .. Correct(alice, t80, 4, Now.AddDays(-6.6)),
            ]);

        var list = await ListAsync(alice, goal);

        Assert.Equal(Mastery.RuleVersion, list.RuleVersion);
        Assert.Equal(
            new[]
            {
                new Entry(t49, 49, "weakness"), new Entry(t50, 50, "building"),
                new Entry(t79, 79, "building"), new Entry(t80, 80, "secure"),
            }.OrderBy(entry => entry.TopicId),
            list.Topics);
        Assert.Equal(new OneTopic(t80, 80, "secure", Mastery.RuleVersion), await FindAsync(alice, t80));
    }

    // Known but never introduced: new. Two wrong of the last three: a weakness although the mastery is 65.
    // 50 -> six correct hard answers 96.2 -> wrong easy 81.8 -> correct medium 86.3 -> wrong medium 64.75 (65).
    [Fact]
    public async Task NewTopic_AndRecentWrongAnswers_ShowTheirState()
    {
        var alice = NewUser("alice");
        var goal = Guid.NewGuid();
        var (untouched, slipping) = (Guid.NewGuid(), Guid.NewGuid());
        var start = Now.AddMinutes(-30);
        var answers = Enumerable.Repeat((true, 3), 6)
            .Append((false, 1)).Append((true, 2)).Append((false, 2))
            .Select((answer, minute) => Record(alice, slipping, answer.Item1, answer.Item2, start.AddMinutes(minute)));
        await SeedAsync(alice, goal, [slipping], [.. answers], known: [untouched]);

        Assert.Equal(new OneTopic(untouched, 50, "new", Mastery.RuleVersion), await FindAsync(alice, untouched));
        Assert.Equal(new OneTopic(slipping, 65, "weakness", Mastery.RuleVersion), await FindAsync(alice, slipping));
        Assert.Contains(new Entry(untouched, 50, "new"), (await ListAsync(alice, goal)).Topics);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static IEnumerable<LearningRecord> Correct(string owner, Guid topic, int count, DateTime from) =>
        Enumerable.Range(0, count).Select(minute => Record(owner, topic, true, 2, from.AddMinutes(minute)));

    private static LearningRecord Record(string owner, Guid topic, bool correct, int difficulty, DateTime answeredAt) =>
        LearningRecord.Create(
            Guid.NewGuid(), owner, topic, Guid.NewGuid(), Guid.NewGuid(), correct, difficulty, 4200, null, Mode.Practice, answeredAt);

    // Introduced long before every answer here, so all of them count. Topics, introductions and records go into the
    // database directly, IntroductionTests and MasteryTests cover the ways through events.
    private async Task SeedAsync(
        string owner, Guid goal, IEnumerable<Guid> introduced, IEnumerable<LearningRecord> records, IEnumerable<Guid>? known = null)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        foreach (var topic in introduced)
        {
            db.KnownTopics.Add(KnownTopic.Create(owner, topic, goal, hidden: false));
            db.TopicIntroductions.Add(TopicIntroduction.Create(owner, topic, Guid.NewGuid(), Now.AddDays(-40)));
        }

        db.KnownTopics.AddRange((known ?? []).Select(topic => KnownTopic.Create(owner, topic, goal, hidden: false)));
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<MasteryList> ListAsync(string user, Guid goal)
    {
        using var response = await GetAsync(user, $"/mastery?learningGoalId={goal}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MasteryList>(TestContext.Current.CancellationToken))!;
    }

    private async Task<OneTopic> FindAsync(string user, Guid topic)
    {
        using var response = await GetAsync(user, $"/mastery/{topic}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OneTopic>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string path)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record MasteryList(int RuleVersion, IReadOnlyList<Entry> Topics);

    private sealed record Entry(Guid TopicId, int Mastery, string State);

    private sealed record OneTopic(Guid TopicId, int Mastery, string State, int RuleVersion);
}