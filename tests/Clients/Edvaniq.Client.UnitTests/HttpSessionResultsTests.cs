using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// #406: the result of a session through the web host, and the waiting until LearningEngine has every answer.
public sealed class HttpSessionResultsTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Session = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";
    private const string Topic = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string Block = "7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e";

    private const string Topics =
        "[{\"topicId\":\"" + Topic + "\",\"before\":{\"mastery\":50,\"state\":\"new\"},\"after\":{\"mastery\":62,\"state\":\"building\"}}]";

    private static readonly SessionResultPolling Fast = new(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(40));

    [Fact]
    public async Task Get_ReadsCountsMasteryAndNextBlock()
    {
        var requests = new List<HttpRequestMessage>();
        var results = Results(requests, Fast, Complete());

        var outcome = await results.GetAsync(Guid.Parse(Session), TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri($"https://edvaniq.test/api/planning/sessions/{Session}/result"), request.RequestUri);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var report = outcome.Value!;
        Assert.Equal((true, 5, 3, 2, true), (report.Completed, report.Answered, report.Correct, report.Wrong, report.Complete));
        Assert.Equal([new MasteryChange(Guid.Parse(Topic), new(50, "new"), new(62, "building"))], report.Topics);
        Assert.Equal(DailyGoalState.Open, report.DailyGoal);
        Assert.Equal(new NextBlock(Guid.Parse(Block), Guid.Parse(Topic), BlockKind.Review, 15), report.Next);
        Assert.True(report.IsSettled);
    }

    // Never a mastery while the report isn't complete, even if one came along.
    [Fact]
    public async Task Get_Incomplete_HasNoMastery()
    {
        var outcome = await Results([], Fast, Pending(withTopics: true)).GetAsync(Guid.Parse(Session), TestContext.Current.CancellationToken);

        Assert.Equal((false, null, false), (outcome.Value!.Complete, outcome.Value.Topics, outcome.Value.IsSettled));
        Assert.Null(outcome.Value.Next);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, CallResult.NotFound)]
    [InlineData(HttpStatusCode.Conflict, CallResult.NotReady)]
    [InlineData(HttpStatusCode.Unauthorized, CallResult.SignedOut)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CallResult.Failed)]
    public async Task Get_Errors_ComeBackAsResults(HttpStatusCode status, CallResult expected)
    {
        var outcome = await Results([], Fast, Problem(status)).GetAsync(Guid.Parse(Session), TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.Result);
    }

    [Fact]
    public async Task Get_ServerError_NamesTheTrace()
    {
        var outcome = await Results([], Fast, Problem(HttpStatusCode.ServiceUnavailable)).GetAsync(Guid.Parse(Session), TestContext.Current.CancellationToken);

        Assert.Equal(TraceId, outcome.TraceId);
    }

    // AC1 and AC2: it asks again until LearningEngine has caught up, each report on the way goes to progress.
    [Fact]
    public async Task Wait_AsksUntilComplete_AndReportsOnTheWay()
    {
        var requests = new List<HttpRequestMessage>();
        var seen = new List<SessionReport>();
        var results = Results(requests, Fast, Pending(), Pending(), Complete());

        var outcome = await results.WaitForCompleteAsync(Guid.Parse(Session), new Collect(seen), TestContext.Current.CancellationToken);

        Assert.Equal(3, requests.Count);
        Assert.Equal([false, false, true], seen.Select(report => report.Complete));
        Assert.True(outcome.Value!.Complete);
    }

    // AC2: past the limit it stops by itself with the last, incomplete report, never waits on.
    [Fact]
    public async Task Wait_StopsAtTheLimit_WithTheIncompleteReport()
    {
        var requests = new List<HttpRequestMessage>();
        var results = Results(requests, Fast, Pending());

        var outcome = await results.WaitForCompleteAsync(Guid.Parse(Session), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.False(outcome.Value!.Complete);
        Assert.InRange(requests.Count, 2, 10);
    }

    // A topic gone from the goal won't ever get a mastery: no waiting for it.
    [Fact]
    public async Task Wait_TopicRemoved_StopsAtOnce()
    {
        var requests = new List<HttpRequestMessage>();
        var results = Results(requests, Fast, Answer($$"""{{Body(complete: false)}},"topicRemoved":true}"""));

        var outcome = await results.WaitForCompleteAsync(Guid.Parse(Session), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(requests);
        Assert.True(outcome.Value!.TopicRemoved);
        Assert.True(outcome.Value.IsSettled);
    }

    [Fact]
    public async Task Wait_Failure_EndsWithIt()
    {
        var requests = new List<HttpRequestMessage>();
        var results = Results(requests, Fast, Pending(), Problem(HttpStatusCode.ServiceUnavailable));

        var outcome = await results.WaitForCompleteAsync(Guid.Parse(Session), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, requests.Count);
        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public void DefaultLimit_StaysWellBelowTheServersWarning() =>
        Assert.True(SessionResultPolling.Default.Limit <= TimeSpan.FromMinutes(1));

    private static string Body(bool complete, bool withTopics = true) => $$"""
        {"sessionId":"{{Session}}","outcome":"completed","answered":5,"correct":3,"wrong":2,"complete":{{(complete ? "true" : "false")}},
         "masteryAvailable":true,
         "topics":{{(withTopics ? Topics : "null")}},
         "dailyGoal":"open",
         "nextStep":{"kind":"block","blockId":"{{Block}}","topicId":"{{Topic}}","blockKind":"review","minutes":15}
        """;

    private static Func<HttpResponseMessage> Complete() => Answer(Body(complete: true) + "}");

    private static Func<HttpResponseMessage> Pending(bool withTopics = false) =>
        Answer(Body(complete: false, withTopics).Replace("\"nextStep\":{\"kind\":\"block\"", "\"nextStep\":{\"kind\":\"bonusReview\"", StringComparison.Ordinal) + "}");

    private static Func<HttpResponseMessage> Answer(string body) =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Func<HttpResponseMessage> Problem(HttpStatusCode status) =>
        () => new HttpResponseMessage(status)
        {
            Content = new StringContent($$"""{"status":{{(int)status}},"traceId":"{{TraceId}}"}""", Encoding.UTF8, "application/problem+json"),
        };

    // Answers in order, then the last one again and again.
    private static HttpSessionResults Results(List<HttpRequestMessage> requests, SessionResultPolling polling, params Func<HttpResponseMessage>[] answers)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(answers);
        return new(
            new HttpClient(new Handler(request =>
            {
                requests.Add(request);
                return (queue.Count > 1 ? queue.Dequeue() : queue.Peek())();
            }))
            {
                BaseAddress = new Uri("https://edvaniq.test/"),
            },
            polling);
    }

    private sealed class Collect(List<SessionReport> seen) : IProgress<SessionReport>
    {
        public void Report(SessionReport value) => seen.Add(value);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}