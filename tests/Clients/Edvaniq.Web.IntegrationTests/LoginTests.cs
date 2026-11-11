using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Web.IntegrationTests;

// A team member signs in with name and password. The web host gets the token from Identity and keeps it in the
// encrypted session cookie, the browser only learns whether it worked. The login ends with its token or with the logout,
// and the pages of the walking skeleton want it.
public sealed partial class LoginTests
{
    [Fact]
    public async Task Login_AsksIdentityWithThePasswordGrant_AndSignsIn()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await LoginAsync(browser);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(browser, "/session/user"));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = StubService.UserName,
                ["password"] = StubService.Password,
                ["client_id"] = "web",
                ["client_secret"] = web.ClientSecret,
            },
            gateway.LastLoginRequest);
    }

    // A wrong name and a wrong password get the same answer, so nobody learns whether an account exists.
    [Fact]
    public async Task WrongNameOrPassword_GetTheSameProblem_AndNoSession()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);

        using var wrongPassword = await LoginAsync(browser, password: "falsches-passwort");
        using var wrongName = await LoginAsync(browser, name: "niemand");

        foreach (var response in new[] { wrongPassword, wrongName })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }

        Assert.Equal(await WithoutTraceAsync(wrongPassword), await WithoutTraceAsync(wrongName));
    }

    [Theory]
    [InlineData("", StubService.Password)]
    [InlineData("  ", StubService.Password)]
    [InlineData(StubService.UserName, "")]
    public async Task MissingNameOrPassword_IsABadRequest_AndNeverReachesIdentity(string name, string password)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await LoginAsync(browser, name, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, gateway.LoginRequests);
    }

    // Identity allows a few attempts a minute per account. The browser learns how long to wait.
    [Fact]
    public async Task TooManyAttemptsAtIdentity_Is429_WithTheWait()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.LoginStatus = StatusCodes.Status429TooManyRequests;
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await LoginAsync(browser);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(42), response.Headers.RetryAfter?.Delta);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task IdentityWithoutAnswer_IsBadGateway_WithATrace(int status)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.LoginStatus = status;
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await LoginAsync(browser);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
        // A retry would count as another attempt against the account.
        Assert.Equal(1, gateway.LoginRequests);
    }

    // Behind nginx every request comes from the gateway of the app's network. The limit counts the browser's address
    // from X-Forwarded-For, so one client cannot use up the attempts of all others.
    [Fact]
    public async Task Logins_AreLimitedPerClientAddress_BehindTheProxy()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder
            .UseSetting("ForwardedHeaders:KnownNetworks:0", "10.1.2.0/24")
            .UseSetting("Login:PerClientPerMinute", "2")
            .ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new FromAddress("10.1.2.1"))));
        using var proxy = web.CreateBrowser(handleCookies: false);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var attempt = await LoginThroughProxyAsync(proxy, client: "203.0.113.7");
            statuses.Add(attempt.StatusCode);
            if (i == 2)
            {
                Assert.NotNull(attempt.Headers.RetryAfter);
            }
        }

        using var other = await LoginThroughProxyAsync(proxy, client: "198.51.100.23");

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
        Assert.Equal(3, gateway.LoginRequests);
    }

    [Fact]
    public async Task Ping_AfterLogin_CarriesTheUsersToken()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        using var login = await LoginAsync(browser);

        var echo = await PingAsync(browser);

        Assert.Equal("Bearer " + StubService.UserToken, echo.Authorization);
        Assert.Equal("", echo.Cookie);
        Assert.Equal(0, gateway.TokenRequests);
    }

    // The logout ends the login on the server too: a copy of the cookie taken before is refused afterwards.
    [Fact]
    public async Task Logout_EndsTheLogin_AlsoForACopyOfTheCookie()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);
        var cookie = await LoginCookieAsync(browser);

        using var logout = await SendAsync(browser, HttpMethod.Delete, "/session", cookie);
        using var user = await SendAsync(browser, HttpMethod.Get, "/session/user", cookie);
        using var ping = await SendAsync(browser, HttpMethod.Post, "/api/planning/ping", cookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value => value.StartsWith(WebSession.CookieName + "=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, user.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ping.StatusCode);
        Assert.Equal(0, gateway.Requests - gateway.LoginRequests);
    }

    // The login lives as long as its token, an hour. It is not renewed on the way, there is no refresh token yet.
    [Fact]
    public async Task Login_EndsWithItsToken()
    {
        var time = new TestTime();
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, time);
        using var browser = web.CreateBrowser();
        using var login = await LoginAsync(browser);

        time.Advance(TimeSpan.FromMinutes(59));
        var before = await StatusAsync(browser, "/session/user");
        time.Advance(TimeSpan.FromMinutes(2));
        var after = await StatusAsync(browser, "/session/user");
        using var ping = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, before);
        Assert.Equal(HttpStatusCode.Unauthorized, after);
        Assert.Equal(HttpStatusCode.Unauthorized, ping.StatusCode);
    }

    // The token is in the cookie only in encrypted form, and nowhere in a page. The cookie stays below the 4 KB a
    // browser keeps per cookie.
    [Fact]
    public async Task TheBrowserCannotReadTheToken()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);

        using var login = await LoginAsync(browser);
        var setCookies = login.Headers.GetValues("Set-Cookie").ToList();
        var cookie = CookieHeader(setCookies);
        using var page = await SendAsync(browser, HttpMethod.Get, "/systemstatus", cookie);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var session = Assert.Single(setCookies);
        Assert.Contains("; httponly", session.ToLowerInvariant());
        Assert.True(Encoding.UTF8.GetByteCount(session) < 4096, $"The cookie has {Encoding.UTF8.GetByteCount(session)} bytes");
        Assert.DoesNotContain(StubService.UserToken, session);
        Assert.DoesNotMatch(Jwt(), session);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain(StubService.UserToken, html);
        Assert.DoesNotMatch(Jwt(), html);
    }

    [Theory]
    [InlineData("/systemstatus", "/login?returnUrl=%2Fsystemstatus")]
    [InlineData("/systemstatus?x=1", "/login?returnUrl=%2Fsystemstatus%3Fx%3D1")]
    public async Task ProtectedPage_WithoutLogin_LeadsToTheLogin_AndBack(string path, string login)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(login, PathAndQuery(response.Headers.Location));
    }

    // The guest session of the smoke test is no login.
    [Fact]
    public async Task ProtectedPage_WithAGuestSession_LeadsToTheLogin()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();
        using var guest = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        using var response = await browser.GetAsync("/systemstatus", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/login?returnUrl=%2Fsystemstatus", PathAndQuery(response.Headers.Location));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(browser, "/session/user"));
    }

    [Fact]
    public async Task ProtectedPage_IsThereAfterLogin_AndGoneAfterLogout()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var login = await LoginAsync(browser);
        using var signedIn = await browser.GetAsync("/systemstatus", TestContext.Current.CancellationToken);
        using var logout = await browser.DeleteAsync("/session", TestContext.Current.CancellationToken);
        using var signedOut = await browser.GetAsync("/systemstatus", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Contains(">Systemstatus</h1>", await signedIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Found, signedOut.StatusCode);
    }

    // The public pages stay without a login.
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/datenschutz")]
    [InlineData("/open-source")]
    [InlineData("/bausteine")]
    public async Task PublicPages_NeedNoLogin(string path)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // deploy/smoke-test.sh, step by step: the shell loads, a guest session starts without Origin as curl sends it, and
    // the ping comes back with the value from the database, carried by the web host's technical token.
    [Fact]
    public async Task SmokeTest_StillPasses_WithoutALogin()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.Answer("POST", "/planning/ping", new { id = 7, pingedAt = DateTimeOffset.UtcNow });
        await using var web = new WebFactory(gateway.Address);
        using var curl = web.CreateBrowser();

        using var shell = await curl.GetAsync("/", TestContext.Current.CancellationToken);
        using var session = await curl.PostAsync("/session", content: null, TestContext.Current.CancellationToken);
        using var ping = await curl.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Contains("_framework/blazor.web.js", await shell.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        var stored = await ping.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(7, stored.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.String, stored.GetProperty("pingedAt").ValueKind);
        Assert.Equal(1, gateway.TokenRequests);
    }

    // Each ended login is kept only until its token would have run out, and never more than the limit.
    [Fact]
    public void EndedLogins_AreForgotten_WhenTheirTokenRunsOut_AndNeverExceedTheLimit()
    {
        var time = new TestTime();
        var ended = new EndedSessions(time);

        ended.End("a", time.GetUtcNow().AddMinutes(10));
        ended.End("b", time.GetUtcNow().AddMinutes(30));
        time.Advance(TimeSpan.FromMinutes(20));
        ended.End("c", time.GetUtcNow().AddMinutes(30));

        Assert.False(ended.HasEnded("a"));
        Assert.True(ended.HasEnded("b"));
        Assert.Equal(2, ended.Count);

        for (var i = 0; i < EndedSessions.Limit + 10; i++)
        {
            ended.End("spam-" + i, time.GetUtcNow().AddHours(1));
        }

        Assert.Equal(EndedSessions.Limit, ended.Count);
        Assert.True(ended.HasEnded("spam-" + (EndedSessions.Limit + 9)));
    }

    internal static Task<HttpResponseMessage> LoginAsync(
        HttpClient browser, string name = StubService.UserName, string password = StubService.Password) =>
        browser.PostAsJsonAsync("/session/login", new { name, password }, TestContext.Current.CancellationToken);

    // Signs in and gives back what the browser would send as Cookie from then on.
    internal static async Task<string> LoginCookieAsync(HttpClient browser)
    {
        using var response = await LoginAsync(browser);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return CookieHeader(response.Headers.GetValues("Set-Cookie"));
    }

    // A redirect stays on this site whether it names the host or not.
    private static string? PathAndQuery(Uri? location) =>
        location is null ? null : new Uri(new Uri("https://localhost"), location).PathAndQuery;

    private static string CookieHeader(IEnumerable<string> setCookies) =>
        string.Join("; ", setCookies.Select(value => value.Split(';')[0]));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient browser, HttpMethod method, string path, string cookie)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        return await browser.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient browser, string path)
    {
        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<Echo> PingAsync(HttpClient browser)
    {
        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(TestContext.Current.CancellationToken);
        problem!.Remove("traceId");
        return JsonSerializer.Serialize(problem);
    }

    // The request as nginx sends it: from the gateway of the app's network, the browser's address in X-Forwarded-For.
    private static async Task<HttpResponseMessage> LoginThroughProxyAsync(HttpClient proxy, string client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/session/login")
        {
            Content = JsonContent.Create(new { name = StubService.UserName, password = "falsch-aber-lang" }),
        };
        request.Headers.Add("X-Forwarded-For", client);
        return await proxy.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // Every request arrives from this address, before the web host's own pipeline sees it.
    private sealed class FromAddress(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    [GeneratedRegex(@"eyJ[\w-]+\.eyJ[\w-]+\.[\w-]+")]
    private static partial Regex Jwt();
}