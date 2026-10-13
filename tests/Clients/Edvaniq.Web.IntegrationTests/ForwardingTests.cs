using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// Calls to the gateway leave from the web host only. The browser's cookie and any Authorization header it sends stay
// behind, and the backend cannot set cookies in the browser.
public sealed class ForwardingTests
{
    [Fact]
    public async Task Api_WithoutSession_IsRejected_AndNeverReachesTheGateway()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, gateway.Requests);
    }

    // The Authorization that arrives is the web host's own token (BackendTokenTests), never the browser's.
    [Fact]
    public async Task Api_WithSession_ReachesTheGateway_WithoutPrefixCookieOrTheBrowsersAuthorization()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "from-the-browser");

        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken);
        Assert.Equal(new Echo("gateway-stub", "POST", "/planning/ping", Authorization: "Bearer stub-token-1", Cookie: ""), echo);
    }

    [Theory]
    [InlineData("POST", "/api/planning/ping")]
    [InlineData("GET", "/api/status")]
    public async Task CookiesFromTheBackend_NeverReachTheBrowser(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.SetsCookie = true;
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        using var response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    // Only the routes in the section ReverseProxy go on to the gateway. Everything else under /api, the token endpoint
    // of Identity included, is unknown to the browser: a problem with 404, and the gateway never hears of it.
    [Theory]
    [InlineData("GET", "/api/planning/me")]
    [InlineData("GET", "/api/planning/ping")]
    [InlineData("POST", "/api/planning/example-items")]
    [InlineData("POST", "/api/content/ping")]
    [InlineData("POST", "/api/identity/connect/token")]
    [InlineData("GET", "/api/status/../planning/me")]
    public async Task OtherApiPaths_NeverReachTheGateway(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = await StartSessionAsync(web);

        using var response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, gateway.Requests);
    }

    // The status check of the shell needs no session: it asks whether the gateway answers at all.
    [Fact]
    public async Task Status_WithoutSession_AsksTheGateway()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/api/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken);
        Assert.Equal("/status", echo?.Path);
    }

    // A 502 stays a 502 for the client in the browser and does not turn into the HTML not-found page.
    [Fact]
    public async Task Status_WhenTheGatewayIsDown_IsBadGateway()
    {
        string address;
        await using (var gateway = await StubService.StartAsync("gateway-stub"))
        {
            address = gateway.Address;
        }

        await using var web = new WebFactory(address);
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/api/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    // The other side of the same rule: an unknown page still gets the not-found page of the shell.
    [Fact]
    public async Task UnknownPage_GetsTheNotFoundPage()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/gibt-es-nicht", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Seite nicht gefunden", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<HttpClient> StartSessionAsync(WebFactory web)
    {
        var browser = web.CreateBrowser();
        using var session = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
        return browser;
    }
}