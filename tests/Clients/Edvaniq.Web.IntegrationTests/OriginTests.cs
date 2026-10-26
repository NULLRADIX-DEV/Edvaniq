using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Edvaniq.Web.IntegrationTests;

// A request that changes something and does not come from the web host's own pages is refused as a problem, before the
// endpoint or the gateway sees it. A caller without a browser sends neither Origin nor Sec-Fetch-Site and passes. The
// test browser's address is https://localhost.
public sealed partial class OriginTests
{
    [Theory]
    [InlineData("cross-site", null)]
    [InlineData("same-site", null)]
    [InlineData("none", null)]
    [InlineData(null, "https://evil.example")]
    [InlineData(null, "http://localhost")]
    [InlineData(null, "https://localhost:8443")]
    [InlineData(null, "null")]
    [InlineData("cross-site", "https://localhost")]
    public async Task Session_FromAnotherSite_IsAProblem_AndStartsNoSession(string? site, string? origin)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await browser.SendAsync(Request(HttpMethod.Post, "/session", site, origin), TestContext.Current.CancellationToken);

        Assert.Matches(TraceId(), await AssertProblemAsync(response));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task EveryMethodThatChangesSomething_FromAnotherSite_IsRefused_AndTheSessionStays(string method)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = await StartSessionAsync(web);

        using var response = await browser.SendAsync(Request(new HttpMethod(method), "/session", "cross-site", null), TestContext.Current.CancellationToken);

        Assert.Matches(TraceId(), await AssertProblemAsync(response));
        using var session = await browser.GetAsync("/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
    }

    // Not even the token request leaves for the gateway, let alone the call itself.
    [Theory]
    [InlineData("cross-site", null)]
    [InlineData(null, "https://evil.example")]
    public async Task Ping_FromAnotherSite_IsRefused_AndNothingReachesTheGateway(string? site, string? origin)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        using var response = await browser.SendAsync(Request(HttpMethod.Post, "/api/planning/ping", site, origin), TestContext.Current.CancellationToken);

        Assert.Matches(TraceId(), await AssertProblemAsync(response));
        Assert.Equal(0, gateway.Requests);
    }

    [Theory]
    [InlineData("same-origin", "https://localhost")]
    [InlineData("same-origin", null)]
    [InlineData(null, "https://localhost")]
    [InlineData(null, "https://LOCALHOST")]
    [InlineData(null, "https://localhost:443")]
    [InlineData(null, null)]
    public async Task FromTheOwnSite_OrWithoutBrowserHeaders_SessionAndPingWork(string? site, string? origin)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var session = await browser.SendAsync(Request(HttpMethod.Post, "/session", site, origin), TestContext.Current.CancellationToken);
        using var ping = await browser.SendAsync(Request(HttpMethod.Post, "/api/planning/ping", site, origin), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        var echo = await ping.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken);
        Assert.Equal("/planning/ping", echo?.Path);
    }

    // Reading changes nothing. Another site may link to the shell or ask for the status.
    [Theory]
    [InlineData("/")]
    [InlineData("/api/status")]
    public async Task Reading_FromAnotherSite_IsNotAffected(string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await browser.SendAsync(Request(HttpMethod.Get, path, "cross-site", "https://evil.example"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The own origin is the public one: scheme from the proxy, but only when the proxy is the one the web host trusts.
    [Fact]
    public async Task BehindTheProxy_TheOriginIsComparedWithThePublicSchemeAndHost()
    {
        await using var web = Web();

        var fromTheProxy = await SendAsync(web, from: "10.1.2.1", origin: "https://shell.example");
        var fromElsewhere = await SendAsync(web, from: "10.1.3.1", origin: "https://shell.example");

        Assert.Equal(StatusCodes.Status204NoContent, fromTheProxy.Response.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, fromElsewhere.Response.StatusCode);
    }

    private static WebFactory Web() => new("http://127.0.0.1:9", configure: builder =>
        builder.UseSetting("ForwardedHeaders:KnownNetworks:0", "10.1.2.0/24"));

    // The request as the host's nginx sends it: plain HTTP, the browser's scheme in X-Forwarded-Proto.
    private static Task<HttpContext> SendAsync(WebFactory web, string from, string origin) =>
        web.Server.SendAsync(context =>
        {
            context.Request.Method = "POST";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("shell.example");
            context.Request.Path = "/session";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers.Origin = origin;
            context.Connection.RemoteIpAddress = IPAddress.Parse(from);
        }, TestContext.Current.CancellationToken);

    private static HttpRequestMessage Request(HttpMethod method, string path, string? site, string? origin)
    {
        var request = new HttpRequestMessage(method, path);
        if (site is not null)
        {
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return request;
    }

    // Returns the trace id the problem names.
    private static async Task<string?> AssertProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.GetProperty("status").GetInt32());
        return problem.GetProperty("traceId").GetString();
    }

    private static async Task<HttpClient> StartSessionAsync(WebFactory web)
    {
        var browser = web.CreateBrowser();
        using var session = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
        return browser;
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceId();
}