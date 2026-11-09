using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The client side of the login: what it sends to the web host and how it reads the answer.
public sealed class HttpWebSessionTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task SignIn_PostsNameAndPasswordAsJson()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();
        var session = Session(requests, new HttpResponseMessage(HttpStatusCode.NoContent));

        var outcome = await session.SignInAsync("ben", "richtig-langes-passwort", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.SignedIn), outcome);
        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/session/login"), request.RequestUri);
        Assert.Equal("""{"name":"ben","password":"richtig-langes-passwort"}""", body);
    }

    [Fact]
    public async Task Unauthorized_IsWrongCredentials()
    {
        var outcome = await Session([], Problem(HttpStatusCode.Unauthorized)).SignInAsync("ben", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.WrongCredentials), outcome);
    }

    [Fact]
    public async Task TooManyRequests_CarriesTheWait()
    {
        var answer = Problem(HttpStatusCode.TooManyRequests);
        answer.Headers.RetryAfter = new(TimeSpan.FromSeconds(42));

        var outcome = await Session([], answer).SignInAsync("ben", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.TooManyAttempts, TimeSpan.FromSeconds(42)), outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task OtherError_IsUnavailable_WithTheTrace(HttpStatusCode code)
    {
        var outcome = await Session([], Problem(code)).SignInAsync("ben", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.Unavailable, TraceId: TraceId), outcome);
    }

    [Fact]
    public async Task NoConnection_IsUnavailable()
    {
        var session = new HttpWebSession(Client((_, _) => throw new HttpRequestException("Connection refused")));

        Assert.Equal(new SignInOutcome(SignInResult.Unavailable), await session.SignInAsync("ben", "x", TestContext.Current.CancellationToken));
        Assert.False(await session.IsSignedInAsync(TestContext.Current.CancellationToken));
        Assert.False(await session.SignOutAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task IsSignedIn_AsksForTheUser(HttpStatusCode code, bool signedIn)
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();

        Assert.Equal(signedIn, await Session(requests, new HttpResponseMessage(code)).IsSignedInAsync(TestContext.Current.CancellationToken));
        Assert.Equal("GET /session/user", Line(Assert.Single(requests).Request));
    }

    [Fact]
    public async Task SignOut_DeletesTheSession()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();

        Assert.True(await Session(requests, new HttpResponseMessage(HttpStatusCode.NoContent)).SignOutAsync(TestContext.Current.CancellationToken));
        Assert.Equal("DELETE /session", Line(Assert.Single(requests).Request));
    }

    private static HttpWebSession Session(List<(HttpRequestMessage Request, string Body)> requests, HttpResponseMessage answer) =>
        new(Client(async (request, cancellationToken) =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request, body));
            return answer;
        }));

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) =>
        new(new Handler(answer)) { BaseAddress = new Uri("https://edvaniq.test/") };

    private static HttpResponseMessage Problem(HttpStatusCode code) =>
        new(code)
        {
            Content = new StringContent($$"""{"status":{{(int)code}},"traceId":"{{TraceId}}"}""", Encoding.UTF8, "application/problem+json"),
        };

    private static string Line(HttpRequestMessage request) => $"{request.Method} {request.RequestUri?.AbsolutePath}";

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}