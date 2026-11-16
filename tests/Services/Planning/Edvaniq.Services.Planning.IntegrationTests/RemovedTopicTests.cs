using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Edvaniq.Services.Planning.IntegrationTests.AssessmentStub;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #412: a topic that leaves its goal (a deleted document, the demo reset) takes its done blocks and a session running on
// it along, so today's plan starts afresh on the same day. Ended sessions stay, their result says the topic is gone
// (#406). The states arrive as TopicsChanged through the broker, like from Knowledge.
public sealed class RemovedTopicTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-TopicsChangedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly DateTime First = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    // The day was done on the old topic. With a new one in its place nothing counts as done: no done block, no minutes,
    // the goal open again and "Jetzt lernen" on the new topic. The result of the old session is still there.
    [Fact]
    public async Task RemovedTopic_GivesAFreshPlanOnTheSameDay()
    {
        var (alice, token) = NewUser("alice");
        var goal = LearningGoal.DefaultIdFor(alice);
        var (gone, added) = (Guid.NewGuid(), Guid.NewGuid());
        Answer(token, (gone, 10), (added, 10));
        await DeliverAsync(new TopicsChanged(alice, goal, First, [gone], []));
        var sessions = new List<(Guid Session, Guid Block)>();
        foreach (var block in (await PlanAsync(token)).Blocks)
        {
            var session = await StartAsync(token, block.Id);
            await PostAsync(token, $"/sessions/{session}/complete");
            sessions.Add((session, block.Id));
        }

        var before = await PlanAsync(token);
        Assert.Equal("reached", before.DailyGoal);

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [added], []));

        var after = await PlanAsync(token);
        Assert.NotEmpty(after.Blocks);
        Assert.All(after.Blocks, block => Assert.Equal((added, false), (block.TopicId, block.Done)));
        Assert.Equal(0, after.Blocks.Where(block => block.Done).Sum(block => block.Minutes));
        Assert.Equal(("open", after.Blocks[0].Id), (after.DailyGoal, after.NextBlockId));
        Assert.Equal(0, Query(db => db.DoneBlocks.IgnoreQueryFilters().Count(block => block.OwnerId == alice)));

        var (last, lastBlock) = sessions[^1];
        service.Assessment.SetAnswers(token, lastBlock, new StubAnswer(Guid.NewGuid(), gone, true, DateTime.UtcNow));
        var (status, result) = await ResultAsync(token, last);
        Assert.Equal((HttpStatusCode.OK, true), (status, result?.TopicRemoved));
    }

    // The running session on the topic is broken off and says so once, a new one can start. Bob, with a session and a
    // done block of his own, keeps both, and so do the blocks of Alice's other topic.
    [Fact]
    public async Task RemovedTopic_BreaksOffTheRunningSession_OfItsOwnerOnly()
    {
        var (alice, token) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        var goal = LearningGoal.DefaultIdFor(alice);
        var (gone, kept, bobs) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Answer(token, (gone, 10), (kept, 40));
        Answer(bobToken, (bobs, 10));
        await DeliverAsync(new TopicsChanged(alice, goal, First, [gone, kept], []));
        await DeliverAsync(new TopicsChanged(bob, LearningGoal.DefaultIdFor(bob), First, [bobs], []));

        var plan = await PlanAsync(token);
        var keptBlock = plan.Blocks.First(block => block.TopicId == kept);
        await PostAsync(token, $"/sessions/{await StartAsync(token, keptBlock.Id)}/complete");
        var running = await StartAsync(token, plan.Blocks.First(block => block.TopicId == gone).Id);
        var bobPlan = await PlanAsync(bobToken);
        await PostAsync(bobToken, $"/sessions/{await StartAsync(bobToken, bobPlan.Blocks[0].Id)}/complete");
        var bobRunning = await StartAsync(bobToken, (await PlanAsync(bobToken)).NextBlockId!.Value);

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [kept], []));

        var aborted = SessionOf(running);
        Assert.Equal((SessionOutcome.Aborted, false), (aborted.Outcome, aborted.IsActive));
        var sent = Assert.Single(EventsOf(running));
        Assert.Equal((alice, false, aborted.EndedAt!.Value), (sent.UserId, sent.Completed, sent.EndedAt));
        Assert.Equal(HttpStatusCode.NoContent, await ActiveStatusAsync(token));
        Assert.True((await PlanAsync(token)).Blocks.Single(block => block.Id == keptBlock.Id).Done);

        Assert.True(SessionOf(bobRunning).IsActive);
        Assert.Empty(EventsOf(bobRunning));
        Assert.Equal(1, Query(db => db.DoneBlocks.IgnoreQueryFilters().Count(block => block.OwnerId == bob)));
    }

    // Hidden is no removal: the running session goes on.
    [Fact]
    public async Task HiddenTopic_LeavesTheRunningSessionAlone()
    {
        var (alice, token) = NewUser("alice");
        var goal = LearningGoal.DefaultIdFor(alice);
        var (hidden, kept) = (Guid.NewGuid(), Guid.NewGuid());
        Answer(token, (hidden, 10), (kept, 40));
        await DeliverAsync(new TopicsChanged(alice, goal, First, [hidden, kept], []));
        var running = await StartAsync(token, (await PlanAsync(token)).Blocks.First(block => block.TopicId == hidden).Id);

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [kept], [hidden]));

        Assert.True(SessionOf(running).IsActive);
        Assert.Empty(EventsOf(running));
    }

    private static (string User, string Token) NewUser(string name)
    {
        var user = $"{name}-{Guid.NewGuid():N}";
        return (user, TestTokens.Create(user));
    }

    // Each topic with a ready question pool and its mastery.
    private void Answer(string token, params (Guid Topic, int Mastery)[] topics)
    {
        foreach (var (topic, mastery) in topics)
        {
            service.Assessment.Set(token, topic);
            service.LearningEngine.Set(token, topic, mastery);
        }
    }

    private Session SessionOf(Guid id) =>
        Query(db => db.Sessions.IgnoreQueryFilters().AsNoTracking().Single(session => session.Id == id));

    private List<SessionCompleted> EventsOf(Guid sessionId) =>
        Query(db => db.Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.Subject == $"{InfrastructureExtensions.ServiceKey}.sessioncompleted")
            .ToList())
            .Select(message => JsonSerializer.Deserialize<SessionCompleted>(message.Payload, JsonSerializerOptions.Web)!)
            .Where(sent => sent.SessionId == sessionId)
            .ToList();

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

    private T Query<T>(Func<PlanningDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    private HttpClient Client(string token)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Plan> PlanAsync(string token)
    {
        using var client = Client(token);
        return (await client.GetFromJsonAsync<Plan>("/plan/today", TestContext.Current.CancellationToken))!;
    }

    private async Task<Guid> StartAsync(string token, Guid blockId)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Started>(TestContext.Current.CancellationToken))!.Id;
    }

    private async Task PostAsync(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpStatusCode> ActiveStatusAsync(string token)
    {
        using var client = Client(token);
        using var response = await client.GetAsync("/sessions/active", TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private async Task<(HttpStatusCode Status, Result? Result)> ResultAsync(string token, Guid session)
    {
        using var client = Client(token);
        using var response = await client.GetAsync($"/sessions/{session}/result", TestContext.Current.CancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Result>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private sealed record Plan(List<PlannedBlock> Blocks, Guid? NextBlockId, string DailyGoal);

    private sealed record PlannedBlock(Guid Id, Guid TopicId, string Kind, int Minutes, bool Done);

    private sealed record Started(Guid Id);

    private sealed record Result(Guid SessionId, bool TopicRemoved);
}