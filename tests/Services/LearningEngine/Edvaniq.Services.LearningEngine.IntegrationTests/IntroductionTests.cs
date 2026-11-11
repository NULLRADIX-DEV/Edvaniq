using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Mode = Edvaniq.Services.Assessment.Contracts.Mode;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// SessionCompleted from Planning introduces topics. From then on only the unit's check questions and later answers
// count. Until Planning sends it (#395) a topic without an introduction counts from its first answer on.
public sealed class IntroductionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task AfterTheIntroduction_OnlyTheUnitAndLaterAnswersCount()
    {
        var alice = NewUser("alice");
        var topic = await KnowAsync(alice);
        var unit = Guid.NewGuid();

        // The transition: practised before any session, the answer counts.
        await DeliverAsync(Answer(alice, topic, Guid.NewGuid(), TimeSpan.FromMinutes(20)));
        Assert.Equal(62, await MasteryAsync(alice, topic));

        // Introduced: that answer came before, the topic stands at the start value again.
        await DeliverAsync(new SessionCompleted(alice, unit, DateTime.UtcNow.AddMinutes(-5), true, [topic]));
        Assert.Equal(50, await MasteryAsync(alice, topic));

        // A check question of the unit, answered before the session ended, counts. So does the list.
        await DeliverAsync(Answer(alice, topic, unit, TimeSpan.FromMinutes(10)));
        Assert.Equal(62, await MasteryAsync(alice, topic));
        Assert.Equal(62, (await ListAsync(alice)).Single(item => item.TopicId == topic).Mastery);
    }

    // The same session again, as a redelivery the inbox drops and under a new message id, and a later one keep the first
    // introduction: the answer between the two still counts.
    [Fact]
    public async Task SessionDeliveredAgain_OrALaterSession_KeepsTheFirstIntroduction()
    {
        var alice = NewUser("alice");
        var topic = await KnowAsync(alice);
        var first = new SessionCompleted(alice, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10), true, [topic]);

        var messageId = await DeliverAsync(first);
        await DeliverAsync(Answer(alice, topic, Guid.NewGuid(), TimeSpan.FromMinutes(5)));
        Broker.DropsDuplicates = false;
        try
        {
            await PublishAsync(first, messageId);
            await DeliverAsync(first);
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }

        await DeliverAsync(new SessionCompleted(alice, Guid.NewGuid(), DateTime.UtcNow, true, [topic]));

        // One consumer, in order: once the later session is through, so is the redelivery.
        Assert.Equal(1, InboxEntries<SessionCompleted>(messageId));
        Assert.Equal(62, await MasteryAsync(alice, topic));
        var introduction = Assert.Single(Query(db => db.TopicIntroductions.IgnoreQueryFilters().Where(item => item.TopicId == topic).ToList()));
        Assert.Equal(first.SessionId, introduction.SessionId);
    }

    // Bob's session names Alice's topic id, that introduces nothing for her.
    [Fact]
    public async Task OtherUsersSession_DoesNotIntroduceTheTopic()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var topic = await KnowAsync(alice);
        await DeliverAsync(Answer(alice, topic, Guid.NewGuid(), TimeSpan.FromMinutes(20)));

        await DeliverAsync(new SessionCompleted(bob, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5), true, [topic]));

        Assert.Equal(62, await MasteryAsync(alice, topic));
        Assert.Equal(0, Query(db => db.TopicIntroductions.IgnoreQueryFilters().Count(item => item.OwnerId == alice)));
    }

    [Fact]
    public async Task DeletedUser_LosesTheirIntroductions()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var topic = Guid.NewGuid();
        await DeliverAsync(new SessionCompleted(alice, Guid.NewGuid(), DateTime.UtcNow, true, [topic]));
        await DeliverAsync(new SessionCompleted(bob, Guid.NewGuid(), DateTime.UtcNow, true, [topic]));

        await DeliverAsync(new UserDeleted(alice));

        Assert.Equal(0, Query(db => db.TopicIntroductions.IgnoreQueryFilters().Count(item => item.OwnerId == alice)));
        Assert.Equal(1, Query(db => db.TopicIntroductions.IgnoreQueryFilters().Count(item => item.OwnerId == bob)));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static AnswerRecorded Answer(string user, Guid topic, Guid session, TimeSpan ago) =>
        new(AnswerRecorded.CurrentVersion, user, topic, Guid.NewGuid(), session, Guid.NewGuid(), true, 2, 4200, null,
            Mode.Practice, DateTime.UtcNow - ago);

    private async Task<Guid> KnowAsync(string owner)
    {
        var topic = KnownTopic.Create(owner, Guid.NewGuid(), Guid.NewGuid(), hidden: false);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.Add(topic);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return topic.TopicId;
    }

    private async Task<int> MasteryAsync(string user, Guid topic)
    {
        using var response = await GetAsync(user, $"/mastery/{topic}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MasteryResponse>(TestContext.Current.CancellationToken))!.Mastery;
    }

    private async Task<IReadOnlyList<TopicEntry>> ListAsync(string user)
    {
        using var response = await GetAsync(user, "/mastery");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ListResponse>(TestContext.Current.CancellationToken))!.Topics;
    }

    private async Task<HttpResponseMessage> GetAsync(string user, string path)
    {
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // Like the senders' dispatchers would, then wait until this service's consumer has it.
    private async Task<Guid> DeliverAsync<TEvent>(TEvent integrationEvent)
    {
        var id = await PublishAsync(integrationEvent);
        var deadline = DateTime.UtcNow + Timeout;
        while (InboxEntries<TEvent>(id) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, $"{typeof(TEvent).Name} was not handled.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return id;
    }

    private async Task<Guid> PublishAsync<TEvent>(TEvent integrationEvent, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        await Broker.PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(TEvent)).Subject, id, JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web), null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private int InboxEntries<TEvent>(Guid messageId)
    {
        var consumer = $"{InfrastructureExtensions.ServiceKey}-{typeof(TEvent).Name}Handler";
        return Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == consumer));
    }

    private T Query<T>(Func<LearningEngineDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>());
    }

    private sealed record MasteryResponse(Guid TopicId, int Mastery, int RuleVersion);

    private sealed record TopicEntry(Guid TopicId, int Mastery);

    private sealed record ListResponse(int RuleVersion, IReadOnlyList<TopicEntry> Topics);
}