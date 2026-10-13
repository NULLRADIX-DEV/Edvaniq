using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Http;

namespace Edvaniq.Web.IntegrationTests;

// /api and /session answer their own errors as problems that name the trace of the request: the id in the log lines
// of web host, gateway and services. Nothing else about the inside gets out, no address and no exception.
public sealed partial class ProblemTests
{
    [Theory]
    [InlineData("POST", "/api/planning/ping")]
    [InlineData("GET", "/session")]
    public async Task WithoutSession_IsAProblemNamingTheTrace(string method, string path)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Matches(TraceId(), await AssertProblemAsync(response, HttpStatusCode.Unauthorized));
    }

    // A client that takes no JSON keeps the bare status, never an error page in its place.
    [Fact]
    public async Task ClientWithoutJson_KeepsTheBareStatus()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/session");
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GatewayDown_IsAProblemNamingTheTrace_WithoutItsAddress()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/api/status", TestContext.Current.CancellationToken);

        Assert.Matches(TraceId(), await AssertProblemAsync(response, HttpStatusCode.BadGateway));
        Assert.DoesNotContain("127.0.0.1", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // The trace the problem names is the one the web host passed on, here with its token request.
    [Fact]
    public async Task NoToken_TheProblemNamesTheTraceTheWebHostPassedOn()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.TokenStatus = StatusCodes.Status400BadRequest;
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        var traceId = await AssertProblemAsync(response, HttpStatusCode.BadGateway);
        Assert.Equal(gateway.LastTraceParent.Split('-')[1], traceId);
    }

    // Returns the trace id the problem names.
    private static async Task<string?> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
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