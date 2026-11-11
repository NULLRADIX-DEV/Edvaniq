using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// POST /learning-goals/default gives every user exactly one default goal, created on the first call.
public sealed class LearningGoalTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task DefaultGoal_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.PostAsync("/learning-goals/default", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FirstCall_CreatesTheDefaultGoal()
    {
        var alice = NewUser("alice");
        Assert.Equal(0, GoalsOf(alice));

        var goal = await DefaultGoalAsync(alice);

        Assert.NotEqual(Guid.Empty, goal);
        Assert.Equal(1, GoalsOf(alice));
    }

    [Fact]
    public async Task SecondCall_GetsTheSameGoal()
    {
        var alice = NewUser("alice");

        var first = await DefaultGoalAsync(alice);
        var second = await DefaultGoalAsync(alice);

        Assert.Equal(first, second);
        Assert.Equal(1, GoalsOf(alice));
    }

    // Each first call finds no goal and inserts one. The key lets one through, the others read it.
    [Fact]
    public async Task CallsAtTheSameTime_CreateExactlyOneGoal()
    {
        var alice = NewUser("alice");

        var goals = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => DefaultGoalAsync(alice)));

        Assert.Single(goals.Distinct());
        Assert.Equal(1, GoalsOf(alice));
    }

    // Users never share a goal: each one gets a goal stored as their own, "Alice" and "alice" included.
    [Fact]
    public async Task OtherUsers_GetGoalsOfTheirOwn()
    {
        var id = Guid.NewGuid().ToString("N");
        string[] users = [$"alice-{id}", $"Alice-{id}", NewUser("bob")];

        var goals = new List<Guid>();
        foreach (var user in users)
        {
            goals.Add(await DefaultGoalAsync(user));
        }

        Assert.Equal(3, goals.Distinct().Count());
        for (var i = 0; i < users.Length; i++)
        {
            Assert.Equal(users[i], OwnerOf(goals[i]));
        }
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private async Task<Guid> DefaultGoalAsync(string user)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));

        using var response = await client.PostAsync("/learning-goals/default", content: null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Goal>(cancellationToken))!.Id;
    }

    private int GoalsOf(string user) =>
        Query(db => db.LearningGoals.IgnoreQueryFilters().Count(goal => goal.OwnerId == user));

    private string OwnerOf(Guid goal) =>
        Query(db => db.LearningGoals.IgnoreQueryFilters().Single(stored => stored.Id == goal).OwnerId);

    private T Query<T>(Func<PlanningDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    private sealed record Goal(Guid Id);
}