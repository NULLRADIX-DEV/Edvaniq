using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The ping either comes back with the value Planning read from its database or it did not get through. A missing session
// is the one failure the client mends itself, and only once.
public sealed class HttpPingTests
{
    private const string Answer = """{"id":42,"pingedAt":"2026-10-13T09:07:14.043846Z"}""";

    [Fact]
    public async Task Success_ReturnsTheValueFromTheDatabase()
    {
        var ping = Ping(Json(Answer));

        var result = await ping.SendAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new PingResult(42, new DateTimeOffset(2026, 10, 13, 9, 7, 14, TimeSpan.Zero).AddTicks(438460)), result);
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

        Assert.Equal(42, result?.Id);
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

        Assert.Null(await ping.SendAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task SessionNotStarted_DidNotGetThrough()
    {
        var requests = new List<HttpRequestMessage>();
        var ping = Ping(requests, Status(HttpStatusCode.Unauthorized), Status(HttpStatusCode.ServiceUnavailable));

        Assert.Null(await ping.SendAsync(TestContext.Current.CancellationToken));
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

        Assert.Null(await ping.SendAsync(TestContext.Current.CancellationToken));
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

        Assert.Null(await ping.SendAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoConnection_DidNotGetThrough()
    {
        var ping = new HttpPing(Client((_, _) => throw new HttpRequestException("Connection refused")));

        Assert.Null(await ping.SendAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoAnswerInTime_DidNotGetThrough()
    {
        var ping = new HttpPing(Client(NeverAsync, timeout: TimeSpan.FromMilliseconds(100)));

        Assert.Null(await ping.SendAsync(TestContext.Current.CancellationToken));
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

    private static string Line(HttpRequestMessage request) => $"{request.Method} {request.RequestUri?.AbsolutePath}";

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}