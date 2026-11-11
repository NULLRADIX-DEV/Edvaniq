using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

using Mode = Edvaniq.Services.Assessment.Contracts.Mode;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// GET /mastery/{topicId}: the mastery of one of the user's topics, worked out from the learning records of the user.
public sealed class MasteryTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-AnswerRecordedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    // Answers a minute old: the value has decayed a hair, 62.5 shows as 62 and 37.5 as 37 (MasteryTests in the unit tests).
    private static readonly TimeSpan AMinute = TimeSpan.FromMinutes(1);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task TopicWithoutAnswers_HasTheStartValue()
    {
        var alice = NewUser("alice");
        var topic = await KnowAsync(alice);

        Assert.Equal(new MasteryResponse(topic, 50, "new", 3), await MasteryAsync(alice, topic));
    }

    [Fact]
    public async Task CorrectAnswer_RaisesIt_WrongAnswer_LowersIt()
    {
        var alice = NewUser("alice");
        var (up, down) = (await KnowAsync(alice), await KnowAsync(alice));

        await DeliverAsync(Answer(alice, up, correct: true));
        await DeliverAsync(Answer(alice, down, correct: false));

        Assert.Equal(62, (await MasteryAsync(alice, up))!.Mastery);
        Assert.Equal(37, (await MasteryAsync(alice, down))!.Mastery);
    }

    // The value is worked out for the time of the request: a correct answer from 90 days ago has lost half, 62.5 -> 31.25.
    [Fact]
    public async Task OldAnswer_HasDecayed()
    {
        var alice = NewUser("alice");
        var topic = await KnowAsync(alice);

        await DeliverAsync(Answer(alice, topic, correct: true, ago: TimeSpan.FromDays(90) + AMinute));

        Assert.Equal(new MasteryResponse(topic, 31, "weakness", 3), await MasteryAsync(alice, topic));
    }

    // Nothing is stored, so the state of the next request already follows every answer (#452). Every value is at least
    // 4 points from 50 and 80 and 0.1 from a rounding edge (decay here is about 0.0005 per minute):
    // hard correct a day ago 67.5 -> 66.98, medium correct 75.24, hard correct 83.90 (secure), then easy wrong ones
    // 71.32 (building) and 60.62: 2 wrong of the last 3, a weakness above 50 (A39).
    [Fact]
    public async Task State_FollowsEveryAnswer()
    {
        var alice = NewUser("alice");
        var topic = await KnowAsync(alice);
        (bool Correct, int Difficulty, TimeSpan Ago, int Mastery, string State)[] steps =
        [
            (true, 3, TimeSpan.FromDays(1), 67, "building"),
            (true, 2, TimeSpan.FromMinutes(4), 75, "building"),
            (true, 3, TimeSpan.FromMinutes(3), 84, "secure"),
            (false, 1, TimeSpan.FromMinutes(2), 71, "building"),
            (false, 1, TimeSpan.FromMinutes(1), 61, "weakness"),
        ];

        foreach (var step in steps)
        {
            await DeliverAsync(Answer(alice, topic, step.Correct, step.Ago, step.Difficulty));

            Assert.Equal(new MasteryResponse(topic, step.Mastery, step.State, 3), await MasteryAsync(alice, topic));
        }
    }

    // The same answer again, under the same and under a new message id, doesn't move the value a second time.
    [Fact]
    public async Task AnswerDeliveredTwice_OrSentTwice_MovesItOnce()
    {
        var alice = NewUser("alice");
        var topic = await KnowAsync(alice);
        var answer = Answer(alice, topic, correct: true);
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(answer, messageId);
            await PublishAsync(answer, messageId);
            var again = await PublishAsync(answer);

            // One consumer, in order: once the second event is through, so is the duplicate.
            await WaitForAsync(() => InboxEntries(again) == 1, "The second event was not handled.");
            Assert.Equal(62, (await MasteryAsync(alice, topic))!.Mastery);
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // Bob answered on the same topic id, his answers don't count for Alice, and he can't see her topic.
    [Fact]
    public async Task OtherUser_DoesNotCount_AndDoesNotSeeIt()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var topic = await KnowAsync(alice);
        await DeliverAsync(Answer(alice, topic, correct: true));
        await DeliverAsync(Answer(bob, topic, correct: false));
        await DeliverAsync(Answer(bob, topic, correct: false));

        Assert.Equal(62, (await MasteryAsync(alice, topic))!.Mastery);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(bob, topic)).StatusCode);
    }

    // An answer can arrive before TopicsChanged names its topic. It's kept and counts as soon as the topic is known.
    [Fact]
    public async Task AnswerToAnUnknownTopic_CountsOnceTheTopicIsKnown()
    {
        var alice = NewUser("alice");
        var topic = Guid.NewGuid();
        await DeliverAsync(Answer(alice, topic, correct: true));

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(alice, topic)).StatusCode);

        await KnowAsync(alice, topic);
        Assert.Equal(62, (await MasteryAsync(alice, topic))!.Mastery);
    }

    // #205: the same answers before and after their topic end in the same mastery and state.
    [Fact]
    public async Task AnswersBeforeOrAfterTheirTopic_EndTheSame()
    {
        var (inOrder, reversed) = (NewUser("alice"), NewUser("bob"));
        (bool Correct, TimeSpan Ago, int Difficulty)[] answers =
            [(true, TimeSpan.FromMinutes(5), 3), (false, TimeSpan.FromMinutes(4), 1), (true, TimeSpan.FromMinutes(3), 2)];

        var first = await KnowAsync(inOrder);
        foreach (var (correct, ago, difficulty) in answers)
        {
            await DeliverAsync(Answer(inOrder, first, correct, ago, difficulty));
        }

        var second = Guid.NewGuid();
        foreach (var (correct, ago, difficulty) in answers)
        {
            await DeliverAsync(Answer(reversed, second, correct, ago, difficulty));
        }

        await KnowAsync(reversed, second);

        var (expected, actual) = (await MasteryAsync(inOrder, first), await MasteryAsync(reversed, second));
        Assert.Equal((expected!.Mastery, expected.State), (actual!.Mastery, actual.State));
    }

    [Fact]
    public async Task Request_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.GetAsync($"/mastery/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static AnswerRecorded Answer(string user, Guid topic, bool correct, TimeSpan? ago = null, int difficulty = 2) =>
        new(AnswerRecorded.CurrentVersion, user, TopicId: topic, ItemId: Guid.NewGuid(), SessionId: Guid.NewGuid(),
            AnswerId: Guid.NewGuid(), correct, difficulty, 4200, null, Mode.Practice, DateTime.UtcNow - (ago ?? AMinute));

    // Topics only come from TopicsChanged, KnownTopicTests covers that way. Here they go into the database directly.
    private async Task<Guid> KnowAsync(string owner, Guid? topicId = null)
    {
        var topic = KnownTopic.Create(owner, topicId ?? Guid.NewGuid(), Guid.NewGuid(), hidden: false);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.Add(topic);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return topic.TopicId;
    }

    private async Task<MasteryResponse?> MasteryAsync(string user, Guid topic)
    {
        using var response = await GetAsync(user, topic);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<MasteryResponse>(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> GetAsync(string user, Guid topic)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/mastery/{topic}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task DeliverAsync(AnswerRecorded answer)
    {
        var messageId = await PublishAsync(answer);
        await WaitForAsync(() => InboxEntries(messageId) == 1, "The answer was not handled.");
    }

    private async Task<Guid> PublishAsync(AnswerRecorded answer, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        await Broker.PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(AnswerRecorded)).Subject, id, JsonSerializer.Serialize(answer, JsonSerializerOptions.Web), null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private int InboxEntries(Guid messageId)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>()
            .Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer);
    }

    // The consumer runs in the background.
    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private sealed record MasteryResponse(Guid TopicId, int Mastery, string State, int RuleVersion);
}