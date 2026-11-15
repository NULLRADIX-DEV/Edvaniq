using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #391: a topic Knowledge hides is in no block of today's plan or the days after, shown again it is planned again, and a
// topic gone leaves no block. The states arrive as TopicsChanged through the broker, like from Knowledge.
public sealed class HiddenTopicPlanTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-TopicsChangedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly DateTime First = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    // AC1 and AC2: hidden, the topic has no block today or later and "Jetzt lernen" doesn't lead to it; the blocks of the
    // other topics keep their ids. Shown again it is in the next plan.
    [Fact]
    public async Task HiddenTopic_IsInNoBlock_AndComesBackWhenShown()
    {
        var (alice, token) = NewUser("alice");
        var goal = LearningGoal.DefaultIdFor(alice);
        var (weak, hidden, strong) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Answer(token, (weak, 20), (hidden, 10), (strong, 60));
        await DeliverAsync(new TopicsChanged(alice, goal, First, [weak, hidden, strong], []));
        var before = await GetAsync<Today>(token, "/plan/today");
        Assert.Contains(before.Blocks, block => block.TopicId == hidden);
        Assert.Equal(hidden, before.Blocks.Single(block => block.Id == before.NextBlockId).TopicId);

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [weak, strong], [hidden]));

        var today = await GetAsync<Today>(token, "/plan/today");
        var week = await GetAsync<Week>(token, "/plan/week");
        Assert.DoesNotContain(today.Blocks, block => block.TopicId == hidden);
        Assert.All(week.Days, day => Assert.DoesNotContain(day.Blocks, block => block.TopicId == hidden));
        Assert.NotEqual(hidden, today.Blocks.Single(block => block.Id == today.NextBlockId).TopicId);
        foreach (var block in today.Blocks)
        {
            var same = before.Blocks.FirstOrDefault(earlier => earlier.TopicId == block.TopicId && earlier.Kind == block.Kind);
            if (same is not null)
            {
                Assert.Equal(same.Id, block.Id);
            }
        }

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(2), [weak, hidden, strong], []));

        Assert.Equal(before.Blocks, (await GetAsync<Today>(token, "/plan/today")).Blocks);
    }

    // AC3: a topic in neither list leaves no block, today or later.
    [Fact]
    public async Task GoneTopic_LeavesNoBlock()
    {
        var (alice, token) = NewUser("alice");
        var goal = LearningGoal.DefaultIdFor(alice);
        var (gone, kept) = (Guid.NewGuid(), Guid.NewGuid());
        Answer(token, (gone, 10), (kept, 40));
        await DeliverAsync(new TopicsChanged(alice, goal, First, [gone, kept], []));
        Assert.Contains((await GetAsync<Today>(token, "/plan/today")).Blocks, block => block.TopicId == gone);

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [kept], []));

        var week = await GetAsync<Week>(token, "/plan/week");
        Assert.All(week.Days, day => Assert.All(day.Blocks, block => Assert.Equal(kept, block.TopicId)));
        Assert.Contains(week.Days[0].Blocks, block => block.TopicId == kept);
    }

    // What was done today stays done, also when its topic is hidden after: only the blocks to come leave it out.
    [Fact]
    public async Task DoneBlock_OfATopicHiddenAfter_StaysDone_WithoutOpenOnes()
    {
        var (alice, token) = NewUser("alice");
        var goal = LearningGoal.DefaultIdFor(alice);
        var (hidden, kept) = (Guid.NewGuid(), Guid.NewGuid());
        Answer(token, (hidden, 10), (kept, 40));
        await DeliverAsync(new TopicsChanged(alice, goal, First, [hidden, kept], []));
        var before = await GetAsync<Today>(token, "/plan/today");
        var done = before.Blocks.First(block => block.TopicId == hidden);
        MarkDone(alice, before, done);

        await DeliverAsync(new TopicsChanged(alice, goal, First.AddSeconds(1), [kept], [hidden]));

        var after = await GetAsync<Today>(token, "/plan/today");
        Assert.Equal([done with { Done = true }], after.Blocks.Where(block => block.TopicId == hidden));
        Assert.All(after.Blocks.Where(block => block.TopicId != hidden), block => Assert.False(block.Done));
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

    // As finishing the block's session will (#395). A done block needs its goal, the start of a session creates it.
    private void MarkDone(string user, Today plan, PlannedBlock block)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
        if (!db.LearningGoals.IgnoreQueryFilters().Any(goal => goal.Id == plan.LearningGoalId))
        {
            db.LearningGoals.Add(LearningGoal.CreateDefault(user, DateTime.UtcNow));
        }

        var kind = block.Kind == "review" ? BlockKind.Review : BlockKind.Learn;
        db.DoneBlocks.Add(DoneBlock.Create(
            block.Id, user, plan.LearningGoalId, plan.StudyDay, new Block(block.TopicId, block.Minutes, kind), DateTime.UtcNow));
        db.SaveChanges();
    }

    private async Task DeliverAsync(TopicsChanged changed)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(changed, JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(TopicsChanged)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow + Timeout;
        while (InboxEntries(id) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The state was not handled.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private int InboxEntries(Guid messageId)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PlanningDbContext>()
            .Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer);
    }

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private sealed record Today(DateOnly StudyDay, Guid LearningGoalId, List<PlannedBlock> Blocks, Guid? NextBlockId);

    private sealed record Week(List<Day> Days);

    private sealed record Day(DateOnly StudyDay, List<PlannedBlock> Blocks);

    private sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);
}