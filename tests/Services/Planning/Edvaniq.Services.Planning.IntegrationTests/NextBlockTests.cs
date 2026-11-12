using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #407: /plan/today names the next open block, a started one first. Done is set in the database as finishing the
// session will (#395); the start goes through POST /sessions.
public sealed class NextBlockTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const int Minutes = 90;

    // AC1 and AC2: block 1 first, after it block 2, and once all are done there is none and the goal is reached.
    [Fact]
    public async Task NextBlock_FollowsThePlan_UntilAllAreDone()
    {
        var (alice, token) = await NewUserAsync("alice");
        var plan = await PlanAsync(token);
        Assert.Equal(plan.Blocks[0].Id, plan.NextBlockId);

        MarkDone(alice, plan, plan.Blocks[0]);
        Assert.Equal(plan.Blocks[1].Id, (await PlanAsync(token)).NextBlockId);

        foreach (var block in plan.Blocks.Skip(1))
        {
            MarkDone(alice, plan, block);
        }

        var done = await PlanAsync(token);
        Assert.Null(done.NextBlockId);
        Assert.Equal("reached", done.DailyGoal);
    }

    // AC2: a user without blocks has no next block either, and no goal.
    [Fact]
    public async Task WithoutBlocks_ThereIsNoNextBlock()
    {
        var token = TestTokens.Create($"bob-{Guid.NewGuid():N}");

        var plan = await PlanAsync(token);

        Assert.Empty(plan.Blocks);
        Assert.Null(plan.NextBlockId);
        Assert.Equal("none", plan.DailyGoal);
    }

    // AC3 as in A61: the first block done, the second untouched, the third started. The third goes on first, once it is
    // done the second.
    [Fact]
    public async Task StartedBlock_GoesOnFirst()
    {
        var (alice, token) = await NewUserAsync("alice");
        var plan = await PlanAsync(token);
        MarkDone(alice, plan, plan.Blocks[0]);
        await StartAsync(token, plan.Blocks[2].Id);

        Assert.Equal(plan.Blocks[2].Id, (await PlanAsync(token)).NextBlockId);

        MarkDone(alice, plan, plan.Blocks[2]);
        Assert.Equal(plan.Blocks[1].Id, (await PlanAsync(token)).NextBlockId);
    }

    // Three topics and a budget with room for all of them.
    private async Task<(string User, string Token)> NewUserAsync(string name)
    {
        var user = $"{name}-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        foreach (var mastery in new[] { 20, 40, 60 })
        {
            var topic = Guid.NewGuid();
            Execute(db =>
            {
                db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
                return db.SaveChanges();
            });
            service.Assessment.Set(token, topic);
            service.LearningEngine.Set(token, topic, mastery);
        }

        using var client = Client(token);
        var week = Enum.GetNames<DayOfWeek>().ToDictionary(day => day.ToLowerInvariant(), _ => Minutes);
        using var response = await client.PutAsJsonAsync("/daily-budget", week, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True((await PlanAsync(token)).Blocks.Count >= 3);
        return (user, token);
    }

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

    private async Task StartAsync(string token, Guid blockId)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<Plan> PlanAsync(string token)
    {
        using var client = Client(token);
        using var response = await client.GetAsync("/plan/today", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Plan>(TestContext.Current.CancellationToken))!;
    }

    private HttpClient Client(string token)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private T Execute<T>(Func<PlanningDbContext, T> action)
    {
        using var scope = service.Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    private sealed record Plan(DateOnly StudyDay, Guid LearningGoalId, List<PlannedBlock> Blocks, string DailyGoal, Guid? NextBlockId);

    private sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);
}