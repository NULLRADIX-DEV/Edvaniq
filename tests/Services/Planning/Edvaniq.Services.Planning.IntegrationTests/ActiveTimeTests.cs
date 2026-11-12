using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #394: the active learning time of a session, counted by the server from the signals start, advance, activity and
// abort, with the moment from its own clock.
public sealed class ActiveTimeTests(ActiveTimeTests.ClockedService service) : IClassFixture<ActiveTimeTests.ClockedService>
{
    // AC1 and AC2: start, +2 min, +10 min (idle, doesn't count), +1 min broken off: 180 s.
    [Fact]
    public async Task Signals_CountOnlyTheGapsUnderTheIdleLimit()
    {
        var token = Seed();
        var session = await StartAsync(token);

        service.Clock.Advance(TimeSpan.FromMinutes(2));
        var (advance, _) = await PostAsync(token, $"/sessions/{session.Id}/advance", new { step = 1 });
        service.Clock.Advance(TimeSpan.FromMinutes(10));
        var activity = await ActivityAsync(token, session.Id);
        service.Clock.Advance(TimeSpan.FromMinutes(1));
        var (_, aborted) = await PostAsync(token, $"/sessions/{session.Id}/abort", new { });
        var (_, read) = await GetAsync(token, $"/sessions/{session.Id}");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.NoContent), (advance, activity));
        Assert.Equal(180, aborted!.ActiveSeconds);
        Assert.Equal(180, read!.ActiveSeconds);
    }

    // AC3: the time stored is UTC and the server's, whatever the client sends.
    [Fact]
    public async Task LastSignal_IsStoredInUtc_FromTheServersClock()
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = Seed(user);
        var session = await StartAsync(token);
        service.Clock.Advance(TimeSpan.FromSeconds(90));
        var now = service.Clock.GetUtcNow().UtcDateTime;

        await ActivityAsync(token, session.Id);
        var stored = Db(db => db.Sessions.IgnoreQueryFilters().AsNoTracking().Single(entry => entry.Id == session.Id));
        var (_, active) = await GetAsync(token, "/sessions/active");

        Assert.Equal(DateTimeKind.Utc, stored.LastActivityAt.Kind);
        Assert.Equal(Stored(now), stored.LastActivityAt);
        Assert.Equal(90, active!.ActiveSeconds);
    }

    // Signals at once: none of them fails, and the gap counts once.
    [Fact]
    public async Task SignalsAtOnce_NeverFail_AndCountTheGapOnce()
    {
        var token = Seed();
        var session = await StartAsync(token);
        service.Clock.Advance(TimeSpan.FromMinutes(1));

        var statuses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ActivityAsync(token, session.Id)));
        var (_, read) = await GetAsync(token, $"/sessions/{session.Id}");

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.NoContent, status));
        Assert.Equal(60, read!.ActiveSeconds);
    }

    // The one stored second loses against the token and stores nothing, the next read has the first one's signal.
    [Fact]
    public async Task SecondOfTwoChanges_IsRefused_ThenReadsTheStoredSession()
    {
        var token = Seed(out var user);
        var session = await StartAsync(token);
        var at = service.Clock.GetUtcNow().UtcDateTime;
        using var first = UserScope(user);
        using var second = UserScope(user);
        var firstSessions = first.ServiceProvider.GetRequiredService<ISessions>();
        var secondSessions = second.ServiceProvider.GetRequiredService<ISessions>();
        var a = (await firstSessions.FindForChangeAsync(session.Id, Cancel))!;
        var b = (await secondSessions.FindForChangeAsync(session.Id, Cancel))!;

        a.RecordActivity(at.AddMinutes(1));
        b.RecordActivity(at.AddMinutes(2));
        var firstSaved = await firstSessions.TrySaveAsync(Cancel);
        var secondSaved = await secondSessions.TrySaveAsync(Cancel);
        var again = (await secondSessions.FindForChangeAsync(session.Id, Cancel))!;

        Assert.Equal((true, false), (firstSaved, secondSaved));
        Assert.Equal((60, Stored(at.AddMinutes(1))), (again.ActiveSeconds, again.LastActivityAt));
    }

    [Fact]
    public async Task Activity_OnAnEndedSession_IsConflict_OnAForeignOne_NotFound()
    {
        var alice = Seed();
        var bob = Seed();
        var session = await StartAsync(alice);

        var foreign = await ActivityAsync(bob, session.Id);
        var missing = await ActivityAsync(alice, Guid.NewGuid());
        await PostAsync(alice, $"/sessions/{session.Id}/abort", new { });
        service.Clock.Advance(TimeSpan.FromMinutes(1));
        var ended = await ActivityAsync(alice, session.Id);
        var (_, read) = await GetAsync(alice, $"/sessions/{session.Id}");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Conflict), (foreign, missing, ended));
        Assert.Equal(0, read!.ActiveSeconds);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Whole microseconds, as the database keeps them.
    private static DateTime Stored(DateTime value) => value.AddTicks(-(value.Ticks % 10));

    private string Seed() => Seed(out _);

    private string Seed(string user)
    {
        var token = TestTokens.Create(user);
        var topic = Guid.NewGuid();
        Db(db =>
        {
            db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
            return db.SaveChanges();
        });
        service.Assessment.Set(token, topic);
        service.LearningEngine.Set(token, topic, 20, "building");
        return token;
    }

    private string Seed(out string user)
    {
        user = $"alice-{Guid.NewGuid():N}";
        return Seed(user);
    }

    private T Db<T>(Func<PlanningDbContext, T> action)
    {
        using var scope = service.Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    // A scope whose current user is the given one, as a request with their token has.
    private IServiceScope UserScope(string user)
    {
        var scope = service.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user)], "test")),
        };
        return scope;
    }

    private HttpClient Client(string token)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Session> StartAsync(string token)
    {
        using var client = Client(token);
        var plan = (await client.GetFromJsonAsync<Plan>("/plan/today", Cancel))!;
        var (status, session) = await PostAsync(token, "/sessions", new { blockId = plan.Blocks[0].Id });
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(0, session!.ActiveSeconds);
        return session;
    }

    private async Task<HttpStatusCode> ActivityAsync(string token, Guid id)
    {
        using var client = Client(token);
        using var response = await client.PostAsync($"/sessions/{id}/activity", null, Cancel);
        return response.StatusCode;
    }

    private async Task<(HttpStatusCode Status, Session? Session)> PostAsync(string token, string path, object body)
    {
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync(path, body, Cancel);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Session>(Cancel))
            : (response.StatusCode, null);
    }

    private async Task<(HttpStatusCode Status, Session? Session)> GetAsync(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.GetAsync(path, Cancel);
        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<Session>(Cancel))
            : (response.StatusCode, null);
    }

    // The service with a clock the test moves, for the sessions only.
    public sealed class ClockedService(MySqlTestServer mysql) : ServiceFactory(mysql)
    {
        public TestTime Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(provider => ActivatorUtilities.CreateInstance<SessionStarts>(provider, (TimeProvider)Clock));
                services.AddScoped(provider => ActivatorUtilities.CreateInstance<SessionChanges>(provider, (TimeProvider)Clock));
            });
        }
    }

    private sealed record Plan(List<PlannedBlock> Blocks);

    private sealed record PlannedBlock(Guid Id);

    private sealed record Session(Guid Id, int ActiveSeconds);
}