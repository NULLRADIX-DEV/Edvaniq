using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #375: blocks done today stay with their status when the plan is worked out again, the open ones follow the current
// mastery, every user sees only their own, and the blocks go with their learning goal and their user. Done is set in
// the database here: the way from outside comes with sessions (#376, #395).
public sealed class DoneBlockTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // AC1: the first block is done, then the mastery changes. The block is still there, done, with its id and minutes,
    // nothing is planned twice, and the day stays within its budget.
    [Fact]
    public async Task DoneBlock_StaysAfterTheMasteryChanged()
    {
        var (alice, token) = NewUser("alice");
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        Seed(alice, token, first, 10);
        Seed(alice, token, second, 60);
        var before = await PlanAsync(token);
        var block = before.Blocks[0];
        MarkDone(alice, before, block);

        service.LearningEngine.Set(token, first, 95);
        service.LearningEngine.Set(token, second, 5);
        var after = await PlanAsync(token);

        Assert.Equal(block with { Done = true }, after.Blocks[0]);
        Assert.Single(after.Blocks, entry => entry.Id == block.Id);
        Assert.All(after.Blocks.Skip(1), entry => Assert.False(entry.Done));
        Assert.True(after.Blocks.Sum(entry => entry.Minutes) <= 30);
        Assert.Equal(after.Blocks, (await PlanAsync(token)).Blocks);
    }

    // AC2: nothing open is stored. The open blocks follow the mastery of every call, the weaker topic gets more time.
    [Fact]
    public async Task OpenBlocks_FollowTheCurrentMastery()
    {
        var (alice, token) = NewUser("alice");
        var (x, y) = (Guid.NewGuid(), Guid.NewGuid());
        Seed(alice, token, x, 10);
        Seed(alice, token, y, 80);
        var before = await PlanAsync(token);

        service.LearningEngine.Set(token, x, 80);
        service.LearningEngine.Set(token, y, 10);
        var after = await PlanAsync(token);

        Assert.True(MinutesOf(before, x) > MinutesOf(before, y));
        Assert.True(MinutesOf(after, y) > MinutesOf(after, x));
        Assert.All(after.Blocks, entry => Assert.False(entry.Done));
        Assert.Equal(0, Query(db => db.DoneBlocks.IgnoreQueryFilters().Count(stored => stored.OwnerId == alice)));
    }

    // AC3: Bob has the same topic. Alice's done block is hers only, Bob's plan has none of her ids.
    [Fact]
    public async Task EachUser_SeesOnlyTheirOwnDoneBlocks()
    {
        var (alice, aliceToken) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        var topic = Guid.NewGuid();
        Seed(alice, aliceToken, topic, 20);
        Seed(bob, bobToken, Guid.NewGuid(), 20);
        var alicePlan = await PlanAsync(aliceToken);
        MarkDone(alice, alicePlan, alicePlan.Blocks[0]);

        var bobPlan = await PlanAsync(bobToken);

        Assert.All(bobPlan.Blocks, entry => Assert.False(entry.Done));
        Assert.DoesNotContain(bobPlan.Blocks, entry => alicePlan.Blocks.Any(own => own.Id == entry.Id));
        Assert.True((await PlanAsync(aliceToken)).Blocks[0].Done);
    }

    // AC4: deleting the learning goal deletes its blocks. There is no endpoint for it yet (#518), the foreign key does
    // it on every way the goal goes.
    [Fact]
    public async Task DeletingTheLearningGoal_RemovesItsBlocks()
    {
        var (alice, token) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        Seed(alice, token, Guid.NewGuid(), 20);
        Seed(bob, bobToken, Guid.NewGuid(), 20);
        var plan = await PlanAsync(token);
        MarkDone(alice, plan, plan.Blocks[0]);
        var bobPlan = await PlanAsync(bobToken);
        MarkDone(bob, bobPlan, bobPlan.Blocks[0]);

        Execute(db => db.LearningGoals.IgnoreQueryFilters().Where(goal => goal.OwnerId == alice).ExecuteDelete());

        Assert.Equal(0, DoneOf(alice));
        Assert.Equal(1, DoneOf(bob));
        Assert.All((await PlanAsync(token)).Blocks, entry => Assert.False(entry.Done));
    }

    // UserDeleted removes goals and done blocks of the user together, whichever the remover takes first.
    [Fact]
    public async Task DeletedUser_LosesTheirDoneBlocks_OthersKeepTheirs()
    {
        var (alice, token) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        Seed(alice, token, Guid.NewGuid(), 20);
        Seed(bob, bobToken, Guid.NewGuid(), 20);
        var plan = await PlanAsync(token);
        MarkDone(alice, plan, plan.Blocks[0]);
        var bobPlan = await PlanAsync(bobToken);
        MarkDone(bob, bobPlan, bobPlan.Blocks[0]);

        var messageId = Guid.NewGuid();
        await service.Services.GetRequiredService<InMemoryTransport>().PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(UserDeleted)).Subject,
                messageId,
                JsonSerializer.Serialize(new UserDeleted(alice), JsonSerializerOptions.Web),
                null),
            TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow + Timeout;
        while (Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId)) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The deletion was not handled.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, DoneOf(alice));
        Assert.Equal(0, Query(db => db.LearningGoals.IgnoreQueryFilters().Count(goal => goal.OwnerId == alice)));
        Assert.Equal(1, DoneOf(bob));
    }

    private static (string User, string Token) NewUser(string name)
    {
        var user = $"{name}-{Guid.NewGuid():N}";
        return (user, TestTokens.Create(user));
    }

    private void Seed(string user, string token, Guid topic, int mastery)
    {
        Execute(db =>
        {
            db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
            return db.SaveChanges();
        });
        service.Assessment.Set(token, topic);
        service.LearningEngine.Set(token, topic, mastery);
    }

    // As finishing the block's session will (#395). A done block needs its goal, the start of a session creates it.
    private void MarkDone(string user, Plan plan, PlannedBlock block) =>
        Execute(db =>
        {
            if (!db.LearningGoals.IgnoreQueryFilters().Any(goal => goal.Id == plan.LearningGoalId))
            {
                db.LearningGoals.Add(LearningGoal.CreateDefault(user, DateTime.UtcNow));
            }

            var kind = block.Kind == "review" ? BlockKind.Review : BlockKind.Learn;
            db.DoneBlocks.Add(DoneBlock.Create(
                block.Id, user, plan.LearningGoalId, plan.StudyDay, new Block(block.TopicId, block.Minutes, kind), DateTime.UtcNow));
            return db.SaveChanges();
        });

    private int DoneOf(string user) => Query(db => db.DoneBlocks.IgnoreQueryFilters().Count(stored => stored.OwnerId == user));

    private static int MinutesOf(Plan plan, Guid topic) =>
        plan.Blocks.Where(block => block.TopicId == topic).Sum(block => block.Minutes);

    private async Task<Plan> PlanAsync(string token)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/plan/today", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Plan>(TestContext.Current.CancellationToken))!;
    }

    private T Query<T>(Func<PlanningDbContext, T> query) => Execute(query);

    private T Execute<T>(Func<PlanningDbContext, T> action)
    {
        using var scope = service.Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    private sealed record Plan(DateOnly StudyDay, Guid LearningGoalId, List<PlannedBlock> Blocks);

    private sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);
}