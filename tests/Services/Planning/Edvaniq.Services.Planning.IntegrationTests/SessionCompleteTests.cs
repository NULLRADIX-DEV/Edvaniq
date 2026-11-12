using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #395: finishing a session marks its block done and sends SessionCompleted in the same save, exactly once. Breaking off
// sends it too, with the topic only if the unit was done.
public sealed class SessionCompleteTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    // AC1: the block shows as done, the next one comes, and the daily goal follows (A49).
    [Fact]
    public async Task Complete_MarksTheBlockDone_AndTheDailyGoalFollows()
    {
        var token = Seed(("building", 20), ("building", 60));
        var plan = await PlanAsync(token);
        Assert.True(plan.Blocks.Count >= 2);
        Assert.Equal("open", plan.DailyGoal);

        var first = await StartAsync(token, plan.Blocks[0].Id);
        var (status, completed) = await PostAsync(token, $"/sessions/{first.Id}/complete");
        var afterFirst = await PlanAsync(token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("completed", first.Id), (completed!.Outcome, completed.Id));
        Assert.NotNull(completed.EndedAt);
        Assert.Equal(completed.Steps.Count - 1, completed.CurrentStep);
        Assert.Equal(HttpStatusCode.NoContent, (await GetAsync(token, "/sessions/active")).Status);
        Assert.True(afterFirst.Blocks.Single(block => block.Id == plan.Blocks[0].Id).Done);
        Assert.Equal(plan.Blocks[1].Id, afterFirst.NextBlockId);
        Assert.Equal("open", afterFirst.DailyGoal);

        foreach (var block in plan.Blocks.Skip(1))
        {
            var next = await StartAsync(token, block.Id);
            await PostAsync(token, $"/sessions/{next.Id}/complete");
        }

        var afterAll = await PlanAsync(token);
        Assert.All(afterAll.Blocks, block => Assert.True(block.Done));
        Assert.Null(afterAll.NextBlockId);
        Assert.Equal("reached", afterAll.DailyGoal);
    }

    // AC2: SessionCompleted names user, session, end and the introduced topic, and goes to the outbox once.
    [Fact]
    public async Task Complete_SendsSessionCompleted_WithTheIntroducedTopic()
    {
        var topic = Guid.NewGuid();
        var (user, token) = SeedTopic(topic, "new", 20);
        var block = (await PlanAsync(token)).Blocks.First(entry => entry.Kind == "learn");
        var session = await StartAsync(token, block.Id);

        var (_, completed) = await PostAsync(token, $"/sessions/{session.Id}/complete");

        var sent = Assert.Single(EventsOf(session.Id));
        Assert.Equal((user, session.Id, true), (sent.UserId, sent.SessionId, sent.Completed));
        Assert.Equal(completed!.EndedAt, sent.EndedAt);
        Assert.Equal([topic], sent.IntroducedTopicIds);
    }

    // AC3: a second finish changes nothing and sends nothing, also after a broken-off session.
    [Fact]
    public async Task SecondComplete_ChangesNothing_AndSendsNothing()
    {
        var token = Seed(("building", 20));
        var session = await StartAsync(token, (await PlanAsync(token)).Blocks[0].Id);
        var (_, first) = await PostAsync(token, $"/sessions/{session.Id}/complete");

        var (status, second) = await PostAsync(token, $"/sessions/{session.Id}/complete");
        var (abortStatus, aborted) = await PostAsync(token, $"/sessions/{session.Id}/abort");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (status, abortStatus));
        Assert.Equal(first, second);
        Assert.Equal(first, aborted);
        Assert.Single(EventsOf(session.Id));
        Assert.Single(DoneBlocksOf(session.BlockId));
    }

    // AC2 and AC3 at once: of two finishes at the same time one gets through, with one done block and one event.
    [Fact]
    public async Task TwoCompletesAtOnce_LeaveOneDoneBlock_AndOneEvent()
    {
        var token = Seed(("building", 20));
        var session = await StartAsync(token, (await PlanAsync(token)).Blocks[0].Id);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => PostAsync(token, $"/sessions/{session.Id}/complete")));

        Assert.All(results, result => Assert.Equal((HttpStatusCode.OK, "completed"), (result.Status, result.Session!.Outcome)));
        Assert.Single(EventsOf(session.Id));
        Assert.Single(DoneBlocksOf(session.BlockId));
    }

    // Broken off, the session still tells which topic it introduced: only if it got past the unit. The block stays open.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task Abort_SendsSessionCompleted_NotCompleted(int step, bool introduced)
    {
        var topic = Guid.NewGuid();
        var (_, token) = SeedTopic(topic, "new", 20);
        var block = (await PlanAsync(token)).Blocks.First(entry => entry.Kind == "learn");
        var session = await StartAsync(token, block.Id);
        await PostAsync(token, $"/sessions/{session.Id}/advance", new { step });

        await PostAsync(token, $"/sessions/{session.Id}/abort");
        await PostAsync(token, $"/sessions/{session.Id}/abort");

        var sent = Assert.Single(EventsOf(session.Id));
        Assert.False(sent.Completed);
        Assert.Equal(introduced ? [topic] : [], sent.IntroducedTopicIds);
        Assert.Empty(DoneBlocksOf(block.Id));
    }

    // Bob can't finish Alice's session.
    [Fact]
    public async Task ForeignSession_IsNotFound()
    {
        var alice = Seed(("building", 20));
        var bob = Seed(("building", 20));
        var session = await StartAsync(alice, (await PlanAsync(alice)).Blocks[0].Id);

        var (status, _) = await PostAsync(bob, $"/sessions/{session.Id}/complete");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(alice, "/sessions/active")).Status);
        Assert.Empty(EventsOf(session.Id));
    }

    private List<SessionCompleted> EventsOf(Guid sessionId) =>
        Query(db => db.Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.Subject == $"{InfrastructureExtensions.ServiceKey}.sessioncompleted")
            .ToList())
            .Select(message => JsonSerializer.Deserialize<SessionCompleted>(message.Payload, JsonSerializerOptions.Web)!)
            .Where(sent => sent.SessionId == sessionId)
            .ToList();

    private List<DoneBlock> DoneBlocksOf(Guid blockId) =>
        Query(db => db.DoneBlocks.IgnoreQueryFilters().AsNoTracking().Where(block => block.Id == blockId).ToList());

    private T Query<T>(Func<PlanningDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
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

    private (string User, string Token) SeedTopic(Guid topic, string state, int mastery)
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        AddTopic(user, token, topic, state, mastery);
        return (user, token);
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

    private async Task<(HttpStatusCode Status, Session? Session)> PostAsync(string token, string path, object? body = null)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync(path, body ?? new { }, TestContext.Current.CancellationToken);
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

    private sealed record Plan(List<PlannedBlock> Blocks, Guid? NextBlockId, string DailyGoal);

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