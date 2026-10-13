using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Edvaniq.Web.IntegrationTests;

// Without a login, the web host shows the backend its own technical token. It gets the token from Identity through
// the gateway, keeps it and sends it only along with the calls whose route asks for it.
public sealed class BackendTokenTests
{
    [Fact]
    public async Task Api_WithSession_CarriesTheTokenFromIdentity()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        var echo = await PingAsync(browser);

        Assert.Equal("Bearer stub-token-1", echo.Authorization);
    }

    [Fact]
    public async Task Token_IsRequestedWithTheWebHostsClientCredentials()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        await PingAsync(browser);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "web",
                ["client_secret"] = web.ClientSecret,
            },
            gateway.LastTokenRequest);
    }

    // The token lives 5 minutes. The web host renews it a minute before the end, not with every call.
    [Fact]
    public async Task Token_IsKept_UntilAMinuteBeforeItExpires()
    {
        var time = new TestTime();
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, time);
        using var browser = await StartSessionAsync(web);

        await PingAsync(browser);
        time.Advance(TimeSpan.FromMinutes(4) - TimeSpan.FromSeconds(1));
        var stillTheFirst = await PingAsync(browser);
        time.Advance(TimeSpan.FromSeconds(2));
        var renewed = await PingAsync(browser);

        Assert.Equal("Bearer stub-token-1", stillTheFirst.Authorization);
        Assert.Equal("Bearer stub-token-2", renewed.Authorization);
        Assert.Equal(2, gateway.TokenRequests);
    }

    // Many calls at once still lead to one token request.
    [Fact]
    public async Task ParallelCalls_ShareOneTokenRequest()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        var echoes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PingAsync(browser)));

        Assert.All(echoes, echo => Assert.Equal("Bearer stub-token-1", echo.Authorization));
        Assert.Equal(1, gateway.TokenRequests);
    }

    // Until there is a login, anyone can start a guest session and send what carries the token. So the web host limits
    // those calls, per client address and in total, and the service's database gets at most so many writes a minute.
    [Fact]
    public async Task TokenCalls_AreLimitedPerClient()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder
            .UseSetting("BackendToken:PerClientPerMinute", "2")
            .UseSetting("BackendToken:TotalPerMinute", "100"));
        using var browser = await StartSessionAsync(web);

        var statuses = await PingStatusesAsync(browser, 3);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        // The third never got to the gateway.
        Assert.Equal(2, gateway.Requests - gateway.TokenRequests);
    }

    [Fact]
    public async Task TokenCalls_AreLimitedInTotal()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder
            .UseSetting("BackendToken:PerClientPerMinute", "100")
            .UseSetting("BackendToken:TotalPerMinute", "2"));
        using var first = await StartSessionAsync(web);
        using var second = await StartSessionAsync(web);

        var statuses = (await PingStatusesAsync(first, 2)).Concat(await PingStatusesAsync(second, 1));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    // The status check carries no token, so no limit holds it back.
    [Fact]
    public async Task Status_IsNotLimited()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder
            .UseSetting("BackendToken:PerClientPerMinute", "1")
            .UseSetting("BackendToken:TotalPerMinute", "1"));
        using var browser = web.CreateBrowser();

        for (var i = 0; i < 3; i++)
        {
            using var response = await browser.GetAsync("/api/status", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // The status check of the shell needs no token, so it does not fetch one either.
    [Fact]
    public async Task Status_GoesWithoutToken()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        var echo = await browser.GetFromJsonAsync<Echo>("/api/status", TestContext.Current.CancellationToken);

        Assert.Equal("", echo?.Authorization);
        Assert.Equal(0, gateway.TokenRequests);
    }

    // Without a token the call would only fail at the service. The web host stops it itself, with a 502 for the client.
    [Fact]
    public async Task NoTokenFromIdentity_IsBadGateway_AndNothingIsForwarded()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.TokenStatus = StatusCodes.Status400BadRequest;
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(gateway.TokenRequests, gateway.Requests);
    }

    // Once Identity hands out tokens again, the next call gets one.
    [Fact]
    public async Task AfterAFailure_TheNextCallTriesAgain()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.TokenStatus = StatusCodes.Status400BadRequest;
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        using var failed = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);
        gateway.TokenStatus = StatusCodes.Status200OK;
        var echo = await PingAsync(browser);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal("Bearer stub-token-2", echo.Authorization);
    }

    // While Identity is down, the standard resilience handler would keep trying for 30 seconds. The web host stops
    // earlier, so the 502 arrives within the 9 seconds it gives the gateway.
    [Fact]
    public async Task IdentityDown_IsBadGateway_WithinNineSeconds()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.TokenStatus = StatusCodes.Status503ServiceUnavailable;
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        var watch = Stopwatch.StartNew();
        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(9));
    }

    // Calls that wait for the same token request share its failure, instead of each trying again one after another.
    [Fact]
    public async Task IdentityDown_ParallelCallsShareOneFailedTokenRequest()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.TokenStatus = StatusCodes.Status503ServiceUnavailable;
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        var watch = Stopwatch.StartNew();
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode));
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(9));
        // One fetch with its retries, not ten.
        Assert.InRange(gateway.TokenRequests, 1, 4);
        Array.ForEach(responses, response => response.Dispose());
    }

    [Fact]
    public void Start_WithoutClientSecret_Fails()
    {
        using var web = new WebFactory("http://127.0.0.1:9", withClientSecret: false);

        var exception = Assert.Throws<OptionsValidationException>(() => web.CreateBrowser());

        Assert.Contains("BackendToken:ClientSecret", exception.Message);
    }

    private static async Task<Echo> PingAsync(HttpClient browser)
    {
        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<List<HttpStatusCode>> PingStatusesAsync(HttpClient browser, int count)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static async Task<HttpClient> StartSessionAsync(WebFactory web)
    {
        var browser = web.CreateBrowser();
        using var session = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
        return browser;
    }
}