using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// User A's data stays invisible to user B, because the owner filter of ServiceDbContext limits every query to the user
// from the token. Every endpoint with a user's token is listed here (IsolationCheck, #271), each kind of resource runs with
// a seed of its own: learning goals, sessions and the example items. Against a real database.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    internal static IsolationCheck Check() => Examples(Sessions(Goals(new IsolationCheck())))
        .Exempt("GET", "/me", "Only the caller's own id from the token.")
        .Exempt("POST", "/ping", "Writes and reads the caller's own ping.")
        .Exempt("GET", "/daily-budget", "Only the caller's own setting, no id in the request.")
        .Exempt("PUT", "/daily-budget", "Only the caller's own setting, no id in the request.")
        .Exempt("GET", "/plan/today", "Only the caller's own state, no id in the request: OtherUser_SeesNothingOfAForeignPlanOrSession.")
        .Exempt("GET", "/plan/week", "Only the caller's own state, no id in the request: OtherUser_SeesNothingOfAForeignPlanOrSession.");

    private static IsolationCheck Goals(IsolationCheck check) => check
        .List("GET", "/learning-goals")
        .Item("GET", "/learning-goals/{id:guid}")
        .Item("PUT", "/learning-goals/{id:guid}", new { name = "Ziel von B" })
        .Item("GET", "/learning-goals/{id:guid}/profile")
        .Exempt("POST", "/learning-goals", "Adds a goal of the caller.")
        .Exempt("POST", "/learning-goals/default", "The caller's own default goal.");

    private static IsolationCheck Sessions(IsolationCheck check) => check
        .Item("GET", "/sessions/{id:guid}")
        .Item("POST", "/sessions/{id:guid}/abort")
        .Item("POST", "/sessions/{id:guid}/complete")
        .Item("POST", "/sessions/{id:guid}/advance", new { step = 1 })
        .Item("POST", "/sessions/{id:guid}/activity")
        .Exempt("POST", "/sessions", "A block id in the body: OtherUser_CannotStartAForeignBlock.")
        .Exempt("GET", "/sessions/active", "Only the caller's own state, no id in the request: OtherUser_SeesNothingOfAForeignPlanOrSession.");

    private static IsolationCheck Examples(IsolationCheck check) => check
        .List("GET", "/examples")
        .Item("GET", "/examples/{id:guid}")
        .Exempt("POST", "/examples", "Adds an item of the caller.");

    [Fact]
    public void EveryEndpoint_IsInTheCheck() => Check().AssertCovers(service.Services);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorChangeTheGoals() => Goals(new IsolationCheck()).RunAsync(service, SeedGoalAsync);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorChangeTheSessions() => Sessions(new IsolationCheck()).RunAsync(service, SeedSessionAsync);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorChangeTheExamples() => Examples(new IsolationCheck()).RunAsync(service, SeedExampleAsync);

    // B's own state shows nothing of A's: neither A's session nor A's goal.
    [Fact]
    public async Task OtherUser_SeesNothingOfAForeignPlanOrSession()
    {
        var alice = NewUser("alice");
        var session = await SeedSessionAsync(alice);
        var goal = await SeedGoalAsync(alice);
        using var bob = ClientFor(NewUser("bob"));

        foreach (var path in new[] { "/sessions/active", "/plan/today", "/plan/week" })
        {
            using var response = await bob.GetAsync(path, TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode}");
            Assert.DoesNotContain(session.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(goal.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(goal.Marker, body, StringComparison.Ordinal);
        }
    }

    // The block of A's session, started by B, is answered like a block that does not exist.
    [Fact]
    public async Task OtherUser_CannotStartAForeignBlock()
    {
        var alice = NewUser("alice");
        await SeedSessionAsync(alice);
        var block = Query(db => db.Sessions.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Single(session => session.OwnerId == alice).BlockId);
        using var bob = ClientFor(NewUser("bob"));

        var foreign = await StartAsync(bob, block);
        var missing = await StartAsync(bob, Guid.NewGuid());

        Assert.False(foreign.Status is HttpStatusCode.OK or HttpStatusCode.Created, foreign.Body);
        Assert.Equal(missing, foreign);
    }

    private async Task<OwnedResource> SeedGoalAsync(string owner)
    {
        using var client = ClientFor(owner);
        var name = $"Ziel {Guid.NewGuid():N}";
        using var created = await client.PostAsJsonAsync("/learning-goals", new { kind = "FreeLearning", name }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var goal = (await created.Content.ReadFromJsonAsync<Item>(TestContext.Current.CancellationToken))!.Id;
        return new OwnedResource(goal, $"/learning-goals/{goal}", name);
    }

    // A session of A in a block of today, as starting it would leave it. It hangs on A's default goal.
    private async Task<OwnedResource> SeedSessionAsync(string owner)
    {
        using (var client = ClientFor(owner))
        using (var goal = await client.PostAsync("/learning-goals/default", null, TestContext.Current.CancellationToken))
        {
            Assert.True(goal.IsSuccessStatusCode, $"{(int)goal.StatusCode}");
        }

        var session = Session.Start(
            Guid.NewGuid(),
            owner,
            LearningGoal.DefaultIdFor(owner),
            DateOnly.FromDateTime(DateTime.UtcNow),
            new DayBlock(Guid.NewGuid(), new Block(Guid.NewGuid(), 15), Done: false),
            DateTime.UtcNow);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
        db.Sessions.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new OwnedResource(session.Id, $"/sessions/{session.Id}", session.Id.ToString());
    }

    private async Task<OwnedResource> SeedExampleAsync(string owner)
    {
        using var client = ClientFor(owner);
        var text = $"Notiz {Guid.NewGuid():N}";
        var item = await CreateAsync(client, text);
        return new OwnedResource(item.Id, $"/examples/{item.Id}", text);
    }

    // The answer without the trace, which differs on every request.
    private static async Task<(HttpStatusCode Status, string Body)> StartAsync(HttpClient client, Guid blockId)
    {
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (body.Length > 0 && JsonNode.Parse(body) is JsonObject problem)
        {
            problem.Remove("traceId");
            body = problem.ToJsonString();
        }

        return (response.StatusCode, body);
    }

    private T Query<T>(Func<PlanningDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    [Fact]
    public async Task OtherUser_DoesNotSeeTheItemsInTheList()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));

        var item = await CreateAsync(alice, "Notiz von Alice");

        Assert.Contains(await ListAsync(alice), listed => listed.Id == item.Id);
        Assert.Empty(await ListAsync(bob));
    }

    [Fact]
    public async Task OtherUser_GetsNotFoundForAForeignItem()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));

        var item = await CreateAsync(alice, "Notiz von Alice");

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(alice, $"/examples/{item.Id}"));
        // A foreign item looks exactly like one that does not exist.
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/examples/{item.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/examples/{Guid.NewGuid()}"));
    }

    // #323: the profile of a goal, which Content asks for, too.
    [Fact]
    public async Task OtherUser_GetsNotFoundForTheProfileOfAForeignGoal()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));
        using var created = await alice.PostAsJsonAsync("/learning-goals", new { kind = "FreeLearning", name = "Von Alice" }, TestContext.Current.CancellationToken);
        var goal = (await created.Content.ReadFromJsonAsync<Item>(TestContext.Current.CancellationToken))!.Id;

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(alice, $"/learning-goals/{goal}/profile"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/learning-goals/{goal}/profile"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(bob, $"/learning-goals/{Guid.NewGuid()}/profile"));
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheItems()
    {
        var id = Guid.NewGuid().ToString("N");
        using var lower = ClientFor($"alice-{id}");
        using var upper = ClientFor($"Alice-{id}");

        var item = await CreateAsync(lower, "Notiz von alice");

        Assert.Empty(await ListAsync(upper));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(lower, $"/examples/{item.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(upper, $"/examples/{item.Id}"));
    }

    // Every test has its own users, so the tests of this class can share one database.
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Item> CreateAsync(HttpClient client, string text)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.PostAsJsonAsync("/examples", new { text }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Item>(cancellationToken))!;
    }

    private static async Task<Item[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Item[]>("/examples", TestContext.Current.CancellationToken))!;

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private sealed record Item(Guid Id, string Text);
}