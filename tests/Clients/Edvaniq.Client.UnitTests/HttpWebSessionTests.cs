using System.Net;
using System.Text;
using System.Text.Json;

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

    // #252: a locked login is a 429 like too many attempts, only with a longer wait.
    [Fact]
    public async Task Locked_IsTooManyAttempts_WithTheLongerWait()
    {
        var answer = Problem(HttpStatusCode.TooManyRequests, "locked", "Zu viele Fehlversuche. Versuch es in 15 Minuten noch einmal.");
        answer.Headers.RetryAfter = new(TimeSpan.FromMinutes(15));

        var outcome = await Session([], answer).SignInAsync("kim@example.org", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.TooManyAttempts, TimeSpan.FromMinutes(15)), outcome);
    }

    [Fact]
    public async Task TooManyRequests_WithoutRetryAfter_WaitsAMinute()
    {
        var outcome = await Session([], new HttpResponseMessage(HttpStatusCode.TooManyRequests))
            .SignInAsync("ben", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.TooManyAttempts, TimeSpan.FromMinutes(1)), outcome);
    }

    [Theory]
    [InlineData("invalid_credentials")]
    [InlineData(null)]
    public async Task Unauthorized_IsWrongCredentials_WhateverTheCode(string? code)
    {
        var outcome = await Session([], Problem(HttpStatusCode.Unauthorized, code, "Anmeldung fehlgeschlagen."))
            .SignInAsync("ben", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.WrongCredentials), outcome);
    }

    [Theory]
    [InlineData("unconfirmed", SignInResult.Unconfirmed)]
    [InlineData("awaiting_approval", SignInResult.AwaitingApproval)]
    public async Task Forbidden_WithAKnownCode_CarriesTheTitle(string code, SignInResult result)
    {
        var outcome = await Session([], Problem(HttpStatusCode.Forbidden, code, "  Ein Satz vom Host.  "))
            .SignInAsync("kim@example.org", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(result, Title: "Ein Satz vom Host."), outcome);
    }

    // A title that is no sentence for the user is dropped, the page has its own then.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Forbidden\nline")]
    public async Task Forbidden_WithoutAUsableTitle_HasNone(string? title)
    {
        var outcome = await Session([], Problem(HttpStatusCode.Forbidden, "awaiting_approval", title))
            .SignInAsync("kim@example.org", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.AwaitingApproval), outcome);
    }

    [Fact]
    public async Task Forbidden_WithALongTitle_HasNone()
    {
        var outcome = await Session([], Problem(HttpStatusCode.Forbidden, "unconfirmed", new string('a', 301)))
            .SignInAsync("kim@example.org", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.Unconfirmed), outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, null)]
    [InlineData(HttpStatusCode.BadRequest, null)]
    [InlineData(HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.Forbidden, "something_new")]
    public async Task OtherError_IsUnavailable_WithTheTrace(HttpStatusCode status, string? code)
    {
        var outcome = await Session([], Problem(status, code)).SignInAsync("ben", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.Unavailable, TraceId: TraceId), outcome);
    }

    // The web host answers a failing Identity with a bare 502, a proxy maybe with a page of its own.
    [Theory]
    [InlineData("", "text/plain")]
    [InlineData("<html>Bad Gateway</html>", "text/html")]
    [InlineData("{kaputt", "application/problem+json")]
    [InlineData("""{"code":7,"title":["x"]}""", "application/problem+json")]
    public async Task BrokenAnswer_IsUnavailable_WithoutATrace(string body, string mediaType)
    {
        var answer = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

        var outcome = await Session([], answer).SignInAsync("ben", "x", TestContext.Current.CancellationToken);

        Assert.Equal(new SignInOutcome(SignInResult.Unavailable), outcome);
    }

    [Fact]
    public async Task NoConnection_IsUnavailable()
    {
        var session = new HttpWebSession(Client((_, _) => throw new HttpRequestException("Connection refused")));

        Assert.Equal(new SignInOutcome(SignInResult.Unavailable), await session.SignInAsync("ben", "x", TestContext.Current.CancellationToken));
        Assert.Null(await session.AccountAsync(TestContext.Current.CancellationToken));
        Assert.False(await session.SignOutAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Account_IsTheKeyTheHostNames()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();
        var answer = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"account":"k3y"}""", Encoding.UTF8, "application/json"),
        };

        Assert.Equal("k3y", await Session(requests, answer).AccountAsync(TestContext.Current.CancellationToken));
        Assert.Equal("GET /session/user", Line(Assert.Single(requests).Request));
    }

    // Signed in without a key counts as signed in, but tells two logins apart no more: the drafts go then.
    [Theory]
    [InlineData(HttpStatusCode.NoContent, "")]
    [InlineData(HttpStatusCode.Unauthorized, null)]
    public async Task Account_WithoutAKey(HttpStatusCode code, string? account)
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();

        Assert.Equal(account, await Session(requests, new HttpResponseMessage(code)).AccountAsync(TestContext.Current.CancellationToken));
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

    private static HttpResponseMessage Problem(HttpStatusCode status, string? code = null, string? title = null) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { status = (int)status, code, title, traceId = TraceId }),
                Encoding.UTF8,
                "application/problem+json"),
        };

    private static string Line(HttpRequestMessage request) => $"{request.Method} {request.RequestUri?.AbsolutePath}";

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}