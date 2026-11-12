using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #393: breaking a session off ends it, the block stays open and comes first, and starting it again goes on from where
// it stopped. Which questions are left is Assessment's (POST /quizzes with the block id).
public sealed class SessionAbortTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    // AC2: the block stays partly done: not done, started, and "Jetzt lernen" goes on with it.
    [Fact]
    public async Task Abort_EndsTheSession_TheBlockStaysOpenAndComesNext()
    {
        var token = Seed(("building", 20), ("building", 60));
        var plan = await PlanAsync(token);
        var block = plan.Blocks[^1];
        var started = await StartAsync(token, block.Id);

        var (status, aborted) = await PostAsync(token, $"/sessions/{started.Id}/abort", new { });
        var after = await PlanAsync(token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("aborted", started.Id), (aborted!.Outcome, aborted.Id));
        Assert.NotNull(aborted.EndedAt);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(token, "/sessions/active"));
        Assert.False(after.Blocks.Single(entry => entry.Id == block.Id).Done);
        Assert.Equal(block.Id, after.NextBlockId);
    }

    [Fact]
    public async Task SecondAbort_ChangesNothing()
    {
        var token = Seed(("building", 20));
        var session = await StartAsync(token, (await PlanAsync(token)).Blocks[0].Id);
        var (_, first) = await PostAsync(token, $"/sessions/{session.Id}/abort", new { });

        var (status, second) = await PostAsync(token, $"/sessions/{session.Id}/abort", new { });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(first, second);
    }

    // AC1 and AC3 on Planning's side: started again, the session goes on from the step it stopped at, with the steps it
    // had (that they stay when the topic got known since is SessionAbortTests in the unit tests).
    [Fact]
    public async Task StartAgain_GoesOnFromTheStepWithTheSameSteps()
    {
        var topic = Guid.NewGuid();
        var token = SeedTopic(topic, "new", 20);
        var block = (await PlanAsync(token)).Blocks.First(entry => entry.Kind == "learn");
        var first = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{first.Id}/advance", new { step = 2 });
        await PostAsync(token, $"/sessions/{first.Id}/abort", new { });

        var again = await StartAsync(token, block.Id);

        Assert.NotEqual(first.Id, again.Id);
        Assert.Equal((2, "running", block.Id), (again.CurrentStep, again.Outcome, again.BlockId));
        Assert.Equal(["review", "introductionUnit", "quiz"], again.Steps.Select(step => step.Kind));
        Assert.Equal(5, again.Steps[^1].Questions);
    }

    // AC3: closing the browser ends nothing. The session is still active, at the step it reached.
    [Fact]
    public async Task BrowserClosed_TheActiveSessionKeepsItsStep()
    {
        var token = Seed(("new", 20));
        var session = await StartAsync(token, (await PlanAsync(token)).Blocks[0].Id);
        await PostAsync(token, $"/sessions/{session.Id}/advance", new { step = 1 });

        var (status, active) = await GetAsync(token, "/sessions/active");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((session.Id, 1, "running"), (active!.Id, active.CurrentStep, active.Outcome));
    }

    // The rule of #376 still holds: with a session running, a start on the broken off block brings back the running one.
    [Fact]
    public async Task StartAgain_WhileAnotherRuns_BringsBackTheRunningOne()
    {
        var token = Seed(("building", 20), ("building", 60));
        var plan = await PlanAsync(token);
        var first = await StartAsync(token, plan.Blocks[0].Id);
        await PostAsync(token, $"/sessions/{first.Id}/abort", new { });
        var running = await StartAsync(token, plan.Blocks[1].Id);

        var (status, again) = await PostAsync(token, "/sessions", new { blockId = plan.Blocks[0].Id });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((running.Id, true), (again!.Id, again.Existing));
    }

    [Fact]
    public async Task Advance_OnlyForward_InRange_AndNotOnAnEndedSession()
    {
        var token = Seed(("new", 20));
        var session = await StartAsync(token, (await PlanAsync(token)).Blocks[0].Id);

        var (forward, atTwo) = await PostAsync(token, $"/sessions/{session.Id}/advance", new { step = 2 });
        var (back, stillTwo) = await PostAsync(token, $"/sessions/{session.Id}/advance", new { step = 1 });
        var (outOfRange, _) = await PostAsync(token, $"/sessions/{session.Id}/advance", new { step = 3 });
        var (missing, _) = await PostAsync(token, $"/sessions/{session.Id}/advance", new { });
        await PostAsync(token, $"/sessions/{session.Id}/abort", new { });
        var (ended, _) = await PostAsync(token, $"/sessions/{session.Id}/advance", new { step = 2 });

        Assert.Equal((HttpStatusCode.OK, 2), (forward, atTwo!.CurrentStep));
        Assert.Equal((HttpStatusCode.OK, 2), (back, stillTwo!.CurrentStep));
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange);
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.Equal(HttpStatusCode.Conflict, ended);
    }

    // Bob can neither break off nor move on Alice's session.
    [Fact]
    public async Task ForeignSession_IsNotFound()
    {
        var alice = Seed(("building", 20));
        var bob = Seed(("building", 20));
        var session = await StartAsync(alice, (await PlanAsync(alice)).Blocks[0].Id);

        var (abort, _) = await PostAsync(bob, $"/sessions/{session.Id}/abort", new { });
        var (advance, _) = await PostAsync(bob, $"/sessions/{session.Id}/advance", new { step = 1 });

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (abort, advance));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(alice, "/sessions/active")).Status);
    }

    // A user with these topics, each with its state and mastery.
    private string Seed(params (string State, int Mastery)[] topics)
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        foreach (var (state, mastery) in topics)
        {
            AddTopic(user, token, Guid.NewGuid(), state, mastery);
        }

        return token;
    }

    private string SeedTopic(Guid topic, string state, int mastery)
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        AddTopic(user, token, topic, state, mastery);
        return token;
    }

    private void AddTopic(string user, string token, Guid topic, string state, int mastery)
    {
        using (var scope = service.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
            db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
            db.SaveChanges();
        }

        service.Assessment.Set(token, topic);
        service.LearningEngine.Set(token, topic, mastery, state);
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

    private async Task<Session> StartAsync(string token, Guid blockId)
    {
        var (status, session) = await PostAsync(token, "/sessions", new { blockId });
        Assert.Equal(HttpStatusCode.Created, status);
        return session!;
    }

    private async Task<(HttpStatusCode Status, Session? Session)> PostAsync(string token, string path, object body)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Session>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private async Task<(HttpStatusCode Status, Session? Session)> GetAsync(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Session>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private async Task<HttpStatusCode> StatusAsync(string token, string path) => (await GetAsync(token, path)).Status;

    private sealed record Plan(List<PlannedBlock> Blocks, Guid? NextBlockId);

    private sealed record PlannedBlock(Guid Id, string Kind, bool Done);

    private sealed record Session(Guid Id, Guid BlockId, DateTime? EndedAt, bool Existing, int CurrentStep, string Outcome, List<Step> Steps)
    {
        // Steps compared by content, so two reads of one session are equal.
        public bool Equals(Session? other) =>
            other is not null
            && (Id, BlockId, EndedAt, Existing, CurrentStep, Outcome) == (other.Id, other.BlockId, other.EndedAt, other.Existing, other.CurrentStep, other.Outcome)
            && Steps.SequenceEqual(other.Steps);

        public override int GetHashCode() => Id.GetHashCode();
    }

    private sealed record Step(string Kind, bool Empty, int? Questions);
}