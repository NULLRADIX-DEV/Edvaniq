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

// #376: POST /sessions starts a session on a block of today's plan. One active session per user, bound to block and
// topic, seen only by its owner.
public sealed class SessionStartTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Start_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // AC2: block and topic come from the plan, the client only names the block.
    [Fact]
    public async Task Session_IsBoundToBlockAndTopic()
    {
        var (alice, token) = NewUser("alice");
        Seed(alice, token, Guid.NewGuid(), 20);
        Seed(alice, token, Guid.NewGuid(), 60);
        var block = (await PlanAsync(token)).Blocks[1];

        var (status, session) = await StartAsync(token, block.Id);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(block.Id, session!.BlockId);
        Assert.Equal(block.TopicId, session.TopicId);
        Assert.Equal(block.Minutes, session.Minutes);
        Assert.Equal(block.Kind, session.Kind);
        Assert.False(session.Existing);
        Assert.Null(session.EndedAt);
        Assert.Equal(session, (await GetAsync<Session>(token, $"/sessions/{session.Id}"))! with { Existing = false });
    }

    // AC1: a second start, for the same or another block, brings back the active session and makes no second one.
    [Fact]
    public async Task SecondStart_BringsBackTheActiveSession()
    {
        var (alice, token) = NewUser("alice");
        Seed(alice, token, Guid.NewGuid(), 20);
        Seed(alice, token, Guid.NewGuid(), 60);
        var plan = await PlanAsync(token);
        var (_, first) = await StartAsync(token, plan.Blocks[0].Id);

        var (sameStatus, same) = await StartAsync(token, plan.Blocks[0].Id);
        var (otherStatus, other) = await StartAsync(token, plan.Blocks[1].Id);

        Assert.Equal(HttpStatusCode.OK, sameStatus);
        Assert.Equal(HttpStatusCode.OK, otherStatus);
        Assert.Equal(first! with { Existing = true }, same);
        Assert.Equal(first with { Existing = true }, other);
        Assert.Equal(1, SessionsOf(alice));
        Assert.Equal(first.Id, (await GetAsync<Session>(token, "/sessions/active"))!.Id);
    }

    // AC1 on two devices at once: the database lets one start through, all get the same session.
    [Fact]
    public async Task StartsAtTheSameTime_LeaveOneSession()
    {
        var (alice, token) = NewUser("alice");
        Seed(alice, token, Guid.NewGuid(), 20);
        Seed(alice, token, Guid.NewGuid(), 60);
        var plan = await PlanAsync(token);

        var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => StartAsync(token, plan.Blocks[index % 2].Id)));

        Assert.Equal(1, SessionsOf(alice));
        Assert.Single(starts, start => start.Status == HttpStatusCode.Created);
        Assert.Single(starts.Select(start => start.Session!.Id).Distinct());
    }

    // AC3: Bob can neither see Alice's session nor change it by starting his own.
    [Fact]
    public async Task OnlyTheOwner_SeesAndTouchesTheSession()
    {
        var (alice, aliceToken) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        Seed(alice, aliceToken, Guid.NewGuid(), 20);
        Seed(bob, bobToken, Guid.NewGuid(), 20);
        var (_, alices) = await StartAsync(aliceToken, (await PlanAsync(aliceToken)).Blocks[0].Id);

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(bobToken, $"/sessions/{alices!.Id}"));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(bobToken, "/sessions/active"));
        var (bobStatus, bobs) = await StartAsync(bobToken, (await PlanAsync(bobToken)).Blocks[0].Id);
        var (stolenStatus, _) = await StartAsync(bobToken, alices.BlockId);

        Assert.Equal(HttpStatusCode.Created, bobStatus);
        Assert.NotEqual(alices.Id, bobs!.Id);
        Assert.Equal(HttpStatusCode.OK, stolenStatus);
        Assert.Equal(alices, (await GetAsync<Session>(aliceToken, $"/sessions/{alices.Id}"))! with { Existing = false });
    }

    // Alice's block id is not in Bob's plan, so he can't start a session on her block.
    [Fact]
    public async Task ForeignBlock_IsNotFound()
    {
        var (alice, aliceToken) = NewUser("alice");
        var (_, bobToken) = NewUser("bob");
        Seed(alice, aliceToken, Guid.NewGuid(), 20);

        var (status, _) = await StartAsync(bobToken, (await PlanAsync(aliceToken)).Blocks[0].Id);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    // The plan was shown, then the mastery changed: the shown block may be gone. The start says so as a problem that
    // tells the client to load the plan again, nothing is guessed.
    [Fact]
    public async Task BlockGoneSinceThePlanWasShown_AsksToLoadThePlanAgain()
    {
        var (alice, token) = NewUser("alice");
        var (weak, strong) = (Guid.NewGuid(), Guid.NewGuid());
        Seed(alice, token, weak, 10);
        Seed(alice, token, strong, 70);
        var shown = await PlanAsync(token);
        var gone = shown.Blocks.Last(block => block.TopicId == weak);

        service.LearningEngine.Set(token, weak, 100);
        var now = await PlanAsync(token);
        Assert.DoesNotContain(now.Blocks, block => block.Id == gone.Id);

        using var client = Client(token);
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId = gone.Id }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Diesen Block gibt es im aktuellen Plan nicht mehr. Lade den Plan neu.", problem.GetProperty("title").GetString());
        Assert.Equal(0, SessionsOf(alice));
    }

    [Fact]
    public async Task DoneBlock_IsAConflict()
    {
        var (alice, token) = NewUser("alice");
        Seed(alice, token, Guid.NewGuid(), 20);
        var plan = await PlanAsync(token);
        var block = plan.Blocks[0];
        Execute(db =>
        {
            db.LearningGoals.Add(LearningGoal.CreateDefault(alice, DateTime.UtcNow));
            db.DoneBlocks.Add(DoneBlock.Create(
                block.Id, alice, plan.LearningGoalId, plan.StudyDay, new Block(block.TopicId, block.Minutes, KindOf(block.Kind)), DateTime.UtcNow));
            return db.SaveChanges();
        });

        var (status, _) = await StartAsync(token, block.Id);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(0, SessionsOf(alice));
    }

    // A session goes with its learning goal and with its user.
    [Fact]
    public async Task Session_GoesWithItsGoal_AndItsUser()
    {
        var (alice, aliceToken) = NewUser("alice");
        var (bob, bobToken) = NewUser("bob");
        var (carol, carolToken) = NewUser("carol");
        foreach (var (user, token) in new[] { (alice, aliceToken), (bob, bobToken), (carol, carolToken) })
        {
            Seed(user, token, Guid.NewGuid(), 20);
            await StartAsync(token, (await PlanAsync(token)).Blocks[0].Id);
        }

        Execute(db => db.LearningGoals.IgnoreQueryFilters().Where(goal => goal.OwnerId == alice).ExecuteDelete());
        var messageId = Guid.NewGuid();
        await service.Services.GetRequiredService<InMemoryTransport>().PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(UserDeleted)).Subject,
                messageId,
                JsonSerializer.Serialize(new UserDeleted(bob), JsonSerializerOptions.Web),
                null),
            TestContext.Current.CancellationToken);
        var deadline = DateTime.UtcNow + Timeout;
        while (Execute(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId)) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The deletion was not handled.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, SessionsOf(alice));
        Assert.Equal(0, SessionsOf(bob));
        Assert.Equal(1, SessionsOf(carol));
    }

    private static (string User, string Token) NewUser(string name)
    {
        var user = $"{name}-{Guid.NewGuid():N}";
        return (user, TestTokens.Create(user));
    }

    private static BlockKind KindOf(string kind) => kind == "review" ? BlockKind.Review : BlockKind.Learn;

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

    private int SessionsOf(string user) => Execute(db => db.Sessions.IgnoreQueryFilters().Count(session => session.OwnerId == user));

    private HttpClient Client(string token)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(HttpStatusCode Status, Session? Session)> StartAsync(string token, Guid blockId)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId }, TestContext.Current.CancellationToken);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Session>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private async Task<HttpStatusCode> StatusAsync(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private async Task<T?> GetAsync<T>(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken);
    }

    private async Task<Plan> PlanAsync(string token) => (await GetAsync<Plan>(token, "/plan/today"))!;

    private T Execute<T>(Func<PlanningDbContext, T> action)
    {
        using var scope = service.Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    private sealed record Plan(DateOnly StudyDay, Guid LearningGoalId, List<PlannedBlock> Blocks);

    private sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);

    private sealed record Session(
        Guid Id, Guid BlockId, Guid TopicId, string Kind, int Minutes, DateOnly StudyDay, Guid LearningGoalId, DateTime StartedAt, DateTime? EndedAt, bool Existing);
}