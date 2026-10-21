using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The ping either comes back with the value Planning read from its database or it did not get through. A missing session
// is the one failure the client mends itself, and only once.
public sealed class HttpPingTests
{
    private const string Answer = """{"id":42,"pingedAt":"2026-10-13T09:07:14.043846Z"}""";
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task Success_ReturnsTheValueFromTheDatabase()
    {
        var ping = Ping(Json(Answer));

        var result = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new PingResult(42, new DateTimeOffset(2026, 10, 13, 9, 7, 14, TimeSpan.Zero).AddTicks(438460)), result.Result);
    }

    [Fact]
    public async Task PostsToThePingOfPlanning_OnTheHost()
    {
        var requests = new List<HttpRequestMessage>();
        var ping = Ping(requests, Json(Answer));

        await ping.SendAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/ping"), request.RequestUri);
    }

    [Fact]
    public async Task WithoutASession_StartsOne_AndTriesOnceMore()
    {
        var requests = new List<HttpRequestMessage>();
        var ping = Ping(requests, Status(HttpStatusCode.Unauthorized), Status(HttpStatusCode.NoContent), Json(Answer));

        var result = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(42, result.Result?.Id);
        Assert.Collection(requests,
            request => Assert.Equal("POST /api/planning/ping", Line(request)),
            request => Assert.Equal("POST /session", Line(request)),
            request => Assert.Equal("POST /api/planning/ping", Line(request)));
    }

    [Fact]
    public async Task StillWithoutASession_DidNotGetThrough_AndDoesNotTryAgain()
    {
        var requests = new List<HttpRequestMessage>();
        var ping = Ping(requests, Status(HttpStatusCode.Unauthorized), Status(HttpStatusCode.NoContent),
            Status(HttpStatusCode.Unauthorized));

        Assert.Null((await ping.SendAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task SessionNotStarted_DidNotGetThrough()
    {
        var requests = new List<HttpRequestMessage>();
        var ping = Ping(requests, Status(HttpStatusCode.Unauthorized), Status(HttpStatusCode.ServiceUnavailable));

        Assert.Null((await ping.SendAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Equal(2, requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ErrorStatus_DidNotGetThrough(HttpStatusCode code)
    {
        var requests = new List<HttpRequestMessage>();
        var ping = Ping(requests, Status(code));

        Assert.Null((await ping.SendAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Single(requests);
    }

    // An answer without the value from the database is no proof that the way there works.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"id":42}""")]
    [InlineData("""{"pingedAt":"2026-10-13T09:07:14Z"}""")]
    [InlineData("<html></html>")]
    public async Task AnswerWithoutTheValue_DidNotGetThrough(string body)
    {
        var ping = Ping(Json(body));

        Assert.Null((await ping.SendAsync(TestContext.Current.CancellationToken)).Result);
    }

    // An error answer of the host is a problem that names the trace of the failed request. The ping passes it on, so the
    // user can tell it to someone who finds the log lines with it.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Problem_PassesTheTraceOn(HttpStatusCode code)
    {
        var ping = Ping(Problem(code, TraceBody(TraceId)));

        var outcome = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PingOutcome.Failed(TraceId), outcome);
    }

    [Fact]
    public async Task Success_NamesNoTrace()
    {
        var outcome = await Ping(Json(Answer)).SendAsync(TestContext.Current.CancellationToken);

        Assert.Null(outcome.TraceId);
    }

    // The trace of the last answer counts: the one the user saw fail, not the 401 before the guest session.
    [Fact]
    public async Task StillWithoutASession_NamesTheTraceOfTheLastAnswer()
    {
        var ping = Ping(Problem(HttpStatusCode.Unauthorized, TraceBody("a" + TraceId[1..])), Status(HttpStatusCode.NoContent),
            Problem(HttpStatusCode.Unauthorized, TraceBody(TraceId)));

        var outcome = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PingOutcome.Failed(TraceId), outcome);
    }

    [Fact]
    public async Task SessionNotStarted_NamesTheTraceOfTheSessionAnswer()
    {
        var ping = Ping(Status(HttpStatusCode.Unauthorized), Problem(HttpStatusCode.ServiceUnavailable, TraceBody(TraceId)));

        var outcome = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PingOutcome.Failed(TraceId), outcome);
    }

    // The 401 before it belonged to another request, so its trace does not stand in for a failure without one.
    [Fact]
    public async Task LastAnswerWithoutATrace_NamesNone()
    {
        var ping = Ping(Problem(HttpStatusCode.Unauthorized, TraceBody(TraceId)), Status(HttpStatusCode.NoContent),
            Status(HttpStatusCode.BadGateway));

        var outcome = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PingOutcome.Failed(), outcome);
    }

    // Only the id of a problem as the host writes it counts: 32 lower-case hex digits and nothing else.
    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"traceId":null}""")]
    [InlineData("""{"traceId":42}""")]
    [InlineData("""{"traceId":"abc"}""")]
    [InlineData("""{"traceId":"4BF92F3577B34DA6A3CE929D0E0E4736"}""")]
    [InlineData("""{"traceId":"4bf92f3577b34da6a3ce929d0e0e47360"}""")]
    [InlineData("""{"traceId":"<b>4bf92f3577b34da6a3ce92</b>"}""")]
    [InlineData("<html>Fehler</html>")]
    public async Task ProblemWithoutAUsableTrace_NamesNone(string body)
    {
        var ping = Ping(Problem(HttpStatusCode.InternalServerError, body));

        var outcome = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PingOutcome.Failed(), outcome);
    }

    // A body that is not a problem, even one with a traceId in it, is not the host speaking.
    [Fact]
    public async Task ErrorAnswerThatIsNoProblem_NamesNoTrace()
    {
        var answer = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(TraceBody(TraceId), Encoding.UTF8, "application/json"),
        };

        var outcome = await Ping(answer).SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PingOutcome.Failed(), outcome);
    }

    [Fact]
    public async Task NoConnection_DidNotGetThrough()
    {
        var ping = new HttpPing(Client((_, _) => throw new HttpRequestException("Connection refused")));

        Assert.Null((await ping.SendAsync(TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task NoAnswerInTime_DidNotGetThrough()
    {
        var ping = new HttpPing(Client(NeverAsync, timeout: TimeSpan.FromMilliseconds(100)));

        Assert.Null((await ping.SendAsync(TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task CancelByTheCaller_IsACancel()
    {
        var ping = new HttpPing(Client(NeverAsync));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ping.SendAsync(cancellation.Token));
    }

    private static async Task<HttpResponseMessage> NeverAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        return Status(HttpStatusCode.OK);
    }

    private static HttpPing Ping(params HttpResponseMessage[] answers) => Ping([], answers);

    // Answers the requests in the given order and records them.
    private static HttpPing Ping(List<HttpRequestMessage> requests, params HttpResponseMessage[] answers)
    {
        var queue = new Queue<HttpResponseMessage>(answers);
        return new HttpPing(Client((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(queue.Dequeue());
        }));
    }

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer, TimeSpan? timeout = null) =>
        new(new Handler(answer))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(100),
        };

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    private static string TraceBody(string traceId) => $$"""{"status":500,"traceId":"{{traceId}}"}""";

    private static string Line(HttpRequestMessage request) => $"{request.Method} {request.RequestUri?.AbsolutePath}";

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}