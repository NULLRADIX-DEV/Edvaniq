using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// #396: starting, moving on and ending a session through the web host.
public sealed class HttpSessionsTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Session = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";
    private const string Topic = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string Block = "7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e";

    private const string Steps =
        """[{"kind":"review","empty":true,"questions":null},{"kind":"introductionUnit","empty":false,"questions":null},{"kind":"quiz","empty":false,"questions":8}]""";

    [Fact]
    public async Task Start_PostsTheBlock_AndReadsTheSession()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var sessions = Sessions(requests, Answer(HttpStatusCode.Created, Body()));

        var outcome = await sessions.StartAsync(Guid.Parse(Block), TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/sessions"), request.RequestUri);
        Assert.Equal($$"""{"blockId":"{{Block}}"}""", body);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var session = outcome.Value!;
        Assert.Equal((Guid.Parse(Session), Guid.Parse(Block), Guid.Parse(Topic)), (session.Id, session.BlockId, session.TopicId));
        Assert.Equal(
            [new SessionStep(SessionStepKind.Review, true, null), new SessionStep(SessionStepKind.IntroductionUnit, false, null), new SessionStep(SessionStepKind.Quiz, false, 8)],
            session.Steps);
        Assert.Equal((1, SessionOutcome.Running, false, false), (session.CurrentStep, session.Outcome, session.Existing, session.Bonus));
        Assert.True(session.Running);
    }

    // The one the user has already comes back with 200 and says so.
    [Fact]
    public async Task Start_TheRunningOne_IsExisting()
    {
        var outcome = await Sessions([], Answer(HttpStatusCode.OK, Body(existing: true, current: 2)))
            .StartAsync(Guid.Parse(Block), TestContext.Current.CancellationToken);

        Assert.Equal((true, 2), (outcome.Value!.Existing, outcome.Value.CurrentStep));
    }

    [Fact]
    public async Task StartBonus_PostsWithoutBody()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var outcome = await Sessions(requests, Answer(HttpStatusCode.Created, Body(bonus: true)))
            .StartBonusAsync(TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/sessions/bonus"), request.RequestUri);
        Assert.Null(body);
        Assert.True(outcome.Value!.Bonus);
    }

    [Fact]
    public async Task Advance_PostsTheStep()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var outcome = await Sessions(requests, Answer(HttpStatusCode.OK, Body(current: 2)))
            .AdvanceAsync(Guid.Parse(Session), 2, TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(new Uri($"https://edvaniq.test/api/planning/sessions/{Session}/advance"), request.RequestUri);
        Assert.Equal("""{"step":2}""", body);
        Assert.Equal(2, outcome.Value!.CurrentStep);
    }

    [Theory]
    [InlineData("complete", "completed", SessionOutcome.Completed)]
    [InlineData("abort", "aborted", SessionOutcome.Aborted)]
    public async Task Ending_PostsToItsAction(string action, string outcomeName, SessionOutcome expected)
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var sessions = Sessions(requests, Answer(HttpStatusCode.OK, Body(outcome: outcomeName)));

        var outcome = action == "complete"
            ? await sessions.CompleteAsync(Guid.Parse(Session), TestContext.Current.CancellationToken)
            : await sessions.AbortAsync(Guid.Parse(Session), TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri($"https://edvaniq.test/api/planning/sessions/{Session}/{action}"), request.RequestUri);
        Assert.Null(body);
        Assert.Equal(expected, outcome.Value!.Outcome);
        Assert.False(outcome.Value.Running);
    }

    // Planning's sentence for a block that is done, a bonus without topic or a session that has ended is for the user.
    [Fact]
    public async Task Conflict_IsRejected_WithPlanningsSentence()
    {
        var outcome = await Sessions([], Problem(HttpStatusCode.Conflict, "Dieser Block ist schon erledigt."))
            .StartAsync(Guid.Parse(Block), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal(["Dieser Block ist schon erledigt."], outcome.Messages);
    }

    [Fact]
    public async Task BlockGone_IsNotFound_WithPlanningsSentence()
    {
        var outcome = await Sessions([], Problem(HttpStatusCode.NotFound, "Diesen Block gibt es im aktuellen Plan nicht mehr. Lade den Plan neu."))
            .StartAsync(Guid.Parse(Block), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.NotFound, outcome.Result);
        Assert.Equal(["Diesen Block gibt es im aktuellen Plan nicht mehr. Lade den Plan neu."], outcome.Messages);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CallResult.SignedOut)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CallResult.Failed)]
    [InlineData(HttpStatusCode.BadRequest, CallResult.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, CallResult.Failed)]
    public async Task OtherErrors_ComeBackAsResults(HttpStatusCode status, CallResult expected)
    {
        var outcome = await Sessions([], Problem(status, null)).StartBonusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.Result);
    }

    [Fact]
    public async Task ServerError_NamesTheTrace()
    {
        var outcome = await Sessions([], Problem(HttpStatusCode.ServiceUnavailable, "Die Bonus-Wiederholung lässt sich gerade nicht zusammenstellen."))
            .StartBonusAsync(TestContext.Current.CancellationToken);

        Assert.Equal((CallResult.Failed, TraceId), (outcome.Result, outcome.TraceId));
    }

    // A step Planning adds later, or an outcome, shows as unknown and doesn't break the session.
    [Fact]
    public async Task UnknownStepAndOutcome_AreNull()
    {
        var outcome = await Sessions([], Answer(HttpStatusCode.OK, Body(steps: """[{"kind":"flashcards","empty":false},{"kind":"quiz","empty":false,"questions":0}]""", current: 0, outcome: "paused")))
            .StartAsync(Guid.Parse(Block), TestContext.Current.CancellationToken);

        Assert.Equal([new SessionStep(null, false, null), new SessionStep(SessionStepKind.Quiz, false, null)], outcome.Value!.Steps);
        Assert.Null(outcome.Value.Outcome);
    }

    // A session the runner couldn't run doesn't get through.
    [Theory]
    [InlineData("[]", 0)]
    [InlineData("null", 0)]
    [InlineData("[null]", 0)]
    [InlineData(Steps, 3)]
    [InlineData(Steps, -1)]
    public async Task UnusableSession_Fails(string steps, int current)
    {
        var outcome = await Sessions([], Answer(HttpStatusCode.OK, Body(steps: steps, current: current)))
            .StartAsync(Guid.Parse(Block), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("<html></html>")]
    public async Task Garbage_Fails(string body)
    {
        var outcome = await Sessions([], Answer(HttpStatusCode.OK, body)).StartAsync(Guid.Parse(Block), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task NoConnection_Fails()
    {
        var sessions = new HttpSessions(new HttpClient(new Handler(_ => throw new HttpRequestException("down")))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
        });

        var outcome = await sessions.CompleteAsync(Guid.Parse(Session), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static string Body(
        bool existing = false, int current = 1, string outcome = "running", bool bonus = false, string steps = Steps) => $$"""
        {"id":"{{Session}}","blockId":"{{Block}}","topicId":"{{Topic}}","kind":"learn","minutes":20,"studyDay":"2026-11-17",
         "learningGoalId":"{{Topic}}","startedAt":"2026-11-17T08:00:00Z","endedAt":null,"existing":{{(existing ? "true" : "false")}},
         "steps":{{steps}},"currentStep":{{current}},"outcome":"{{outcome}}","activeSeconds":0,"bonus":{{(bonus ? "true" : "false")}}}
        """;

    private static Func<HttpResponseMessage> Answer(HttpStatusCode status, string body) =>
        () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Func<HttpResponseMessage> Problem(HttpStatusCode status, string? title) =>
        () => new HttpResponseMessage(status)
        {
            Content = new StringContent(
                title is null
                    ? $$"""{"status":{{(int)status}},"traceId":"{{TraceId}}"}"""
                    : $$"""{"title":"{{title}}","status":{{(int)status}},"traceId":"{{TraceId}}"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

    private static HttpSessions Sessions(List<(HttpRequestMessage Request, string? Body)> requests, Func<HttpResponseMessage> answer) =>
        new(new HttpClient(new Handler(request =>
        {
            requests.Add((request, request.Content?.ReadAsStringAsync().GetAwaiter().GetResult()));
            return answer();
        }))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
        });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}