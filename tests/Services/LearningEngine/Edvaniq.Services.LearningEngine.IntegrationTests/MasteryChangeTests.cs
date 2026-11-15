using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// #403: GET /mastery/changes, where the topics stood at the start and the end of a session and which of its answers
// have arrived, for Planning's session result.
public sealed class MasteryChangeTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly DateTime From = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddMinutes(20);

    [Fact]
    public async Task GivesBeforeAndAfter_AndTheAnswersMissing()
    {
        var alice = NewUser("alice");
        var topic = Guid.NewGuid();
        var earlier = Record(alice, topic, From.AddDays(-2), correct: false);
        var session = new[] { Record(alice, topic, From.AddMinutes(3), correct: true), Record(alice, topic, From.AddMinutes(6), correct: true) };
        await SeedAsync([Topic(alice, topic)], [earlier, .. session]);
        var missing = Guid.NewGuid();

        var changes = await ChangesAsync(alice, Query([topic], [.. session.Select(record => record.AnswerId), missing]));

        Assert.Equal(Mastery.RuleVersion, changes.RuleVersion);
        Assert.Equal(1, changes.MissingAnswers);
        var change = Assert.Single(changes.Topics);
        Assert.Equal(Entry(TopicStates.Assess([earlier], null, From)), change.Before);
        Assert.Equal(Entry(TopicStates.Assess([earlier, .. session], null, To)), change.After);
        Assert.False(change.Introduced);
    }

    [Fact]
    public async Task Introduction_IsNamed()
    {
        var alice = NewUser("alice");
        var topic = Guid.NewGuid();
        var unit = Guid.NewGuid();
        await SeedAsync([Topic(alice, topic)], [Record(alice, topic, From.AddMinutes(3), correct: true, session: unit)]);
        using (var scope = service.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
            db.TopicIntroductions.Add(TopicIntroduction.Create(alice, topic, unit, To));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var change = Assert.Single((await ChangesAsync(alice, Query([topic], []))).Topics);

        Assert.True(change.Introduced);
        Assert.Equal("new", change.Before!.State);
        Assert.NotEqual("new", change.After!.State);
    }

    // Bob asks with Alice's topic and answer: the topic is left out, the answer missing, like an unknown one.
    [Fact]
    public async Task ForeignTopicsAndAnswers_CountAsUnknown()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var topic = Guid.NewGuid();
        var record = Record(alice, topic, From.AddMinutes(3), correct: true);
        await SeedAsync([Topic(alice, topic)], [record]);

        var changes = await ChangesAsync(bob, Query([topic, Guid.NewGuid()], [record.AnswerId]));

        Assert.Empty(changes.Topics);
        Assert.Equal(1, changes.MissingAnswers);
    }

    [Theory]
    [InlineData("from=2026-11-09T09:00:00Z&to=2026-11-09T09:20:00Z")]
    [InlineData("topicId=x&from=2026-11-09T09:00:00Z&to=2026-11-09T09:20:00Z")]
    [InlineData("topicId=00000000-0000-0000-0000-000000000001&to=2026-11-09T09:20:00Z")]
    [InlineData("topicId=00000000-0000-0000-0000-000000000001&from=gestern&to=2026-11-09T09:20:00Z")]
    [InlineData("topicId=00000000-0000-0000-0000-000000000001&from=2026-11-09T09:20:00Z&to=2026-11-09T09:00:00Z")]
    public async Task BrokenQuery_IsABadRequest(string query)
    {
        using var response = await GetAsync(NewUser("alice"), query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnswerLimit_HoldsAHundredAndRejectsMore()
    {
        var alice = NewUser("alice");
        var topic = Guid.NewGuid();

        using var atLimit = await GetAsync(alice, Query([topic], [.. Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())]));
        using var over = await GetAsync(alice, Query([topic], [.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())]));

        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static KnownTopic Topic(string owner, Guid topicId) => KnownTopic.Create(owner, topicId, Guid.NewGuid(), hidden: false);

    private static LearningRecord Record(string owner, Guid topicId, DateTime answeredAt, bool correct, Guid? session = null) =>
        LearningRecord.Create(Guid.NewGuid(), owner, topicId, Guid.NewGuid(), session, correct, 2, 4200, null, Mode.Practice, answeredAt);

    private static StandingDto Entry(TopicStanding standing) =>
        new(standing.Mastery, standing.State switch
        {
            TopicState.New => "new",
            TopicState.Weakness => "weakness",
            TopicState.Building => "building",
            _ => "secure",
        });

    private static string Query(IEnumerable<Guid> topics, IEnumerable<Guid> answers) =>
        string.Join('&', [
            .. topics.Select(topic => $"topicId={topic}"),
            .. answers.Select(answer => $"answerId={answer}"),
            "from=2026-11-09T09:00:00Z",
            "to=2026-11-09T09:20:00Z",
        ]);

    private async Task SeedAsync(IEnumerable<KnownTopic> topics, IEnumerable<LearningRecord> records)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.AddRange(topics);
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<ChangesDto> ChangesAsync(string user, string query)
    {
        using var response = await GetAsync(user, query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChangesDto>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string query)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mastery/changes?" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record ChangesDto(int RuleVersion, int MissingAnswers, IReadOnlyList<ChangeDto> Topics);

    private sealed record ChangeDto(Guid TopicId, StandingDto? Before, StandingDto? After, bool Introduced);

    private sealed record StandingDto(int Mastery, string State);
}