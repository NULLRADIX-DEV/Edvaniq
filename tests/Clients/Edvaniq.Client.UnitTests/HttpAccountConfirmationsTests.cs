using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// A new confirmation link from the login page: what goes to the web host and how its answer reads.
public sealed class HttpAccountConfirmationsTests
{
    [Fact]
    public async Task Resend_PostsTheAddressAsJson()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();

        var outcome = await Confirmations(requests, Accepted("""{"mailDelayed":false}"""))
            .ResendAsync("kim@example.org", TestContext.Current.CancellationToken);

        Assert.Equal(new ResendOutcome(ResendResult.Sent), outcome);
        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/identity/accounts/confirmations/resend"), request.RequestUri);
        Assert.Equal("""{"email":"kim@example.org"}""", body);
    }

    [Fact]
    public async Task MailDelayed_IsPassedOn()
    {
        var outcome = await Confirmations([], Accepted("""{"mailDelayed":true}"""))
            .ResendAsync("kim@example.org", TestContext.Current.CancellationToken);

        Assert.Equal(new ResendOutcome(ResendResult.Sent, MailDelayed: true), outcome);
    }

    // The host took it, an answer the client cannot read only loses the note about a delay.
    [Theory]
    [InlineData("")]
    [InlineData("{kaputt")]
    [InlineData("""{"mailDelayed":"ja"}""")]
    [InlineData("null")]
    public async Task UnreadableAnswer_StillCountsAsSent(string body)
    {
        var outcome = await Confirmations([], Accepted(body)).ResendAsync("kim@example.org", TestContext.Current.CancellationToken);

        Assert.Equal(new ResendOutcome(ResendResult.Sent), outcome);
    }

    [Fact]
    public async Task TooManyRequests_IsLimited_WithTheWait()
    {
        var answer = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        answer.Headers.RetryAfter = new(TimeSpan.FromMinutes(7));

        var outcome = await Confirmations([], answer).ResendAsync("kim@example.org", TestContext.Current.CancellationToken);

        Assert.Equal(new ResendOutcome(ResendResult.Limited, RetryAfter: TimeSpan.FromMinutes(7)), outcome);
    }

    [Fact]
    public async Task TooManyRequests_WithoutRetryAfter_HasNoWait()
    {
        var outcome = await Confirmations([], new HttpResponseMessage(HttpStatusCode.TooManyRequests))
            .ResendAsync("kim@example.org", TestContext.Current.CancellationToken);

        Assert.Equal(new ResendOutcome(ResendResult.Limited), outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OtherError_IsUnavailable(HttpStatusCode status)
    {
        var outcome = await Confirmations([], new HttpResponseMessage(status)).ResendAsync("kim@example.org", TestContext.Current.CancellationToken);

        Assert.Equal(new ResendOutcome(ResendResult.Unavailable), outcome);
    }

    [Fact]
    public async Task NoConnection_IsUnavailable()
    {
        var confirmations = new HttpAccountConfirmations(Client((_, _) => throw new HttpRequestException("Connection refused")));

        Assert.Equal(
            new ResendOutcome(ResendResult.Unavailable),
            await confirmations.ResendAsync("kim@example.org", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Timeout_IsUnavailable()
    {
        var confirmations = new HttpAccountConfirmations(Client((_, _) => throw new TaskCanceledException("timeout")));

        Assert.Equal(
            new ResendOutcome(ResendResult.Unavailable),
            await confirmations.ResendAsync("kim@example.org", TestContext.Current.CancellationToken));
    }

    private static HttpResponseMessage Accepted(string body) =>
        new(HttpStatusCode.Accepted) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpAccountConfirmations Confirmations(List<(HttpRequestMessage Request, string Body)> requests, HttpResponseMessage answer) =>
        new(Client(async (request, cancellationToken) =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request, body));
            return answer;
        }));

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) =>
        new(new Handler(answer)) { BaseAddress = new Uri("https://edvaniq.test/") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}