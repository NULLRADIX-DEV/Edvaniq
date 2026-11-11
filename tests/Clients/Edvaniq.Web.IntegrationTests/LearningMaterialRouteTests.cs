using System.Net;
using System.Net.Http.Json;
using System.Text;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;

namespace Edvaniq.Web.IntegrationTests;

// The topics page reaches Content and Knowledge only through these routes of the web host, only signed in, and only
// with the methods it needs. The backend sees the user's token, never the browser's cookie.
public sealed class LearningMaterialRouteTests
{
    [Theory]
    [InlineData("GET", "/api/content/documents", "/content/documents")]
    [InlineData("POST", "/api/content/documents", "/content/documents")]
    [InlineData("GET", "/api/knowledge/topics", "/knowledge/topics")]
    public async Task AfterLogin_TheCallReachesTheService_WithTheUsersToken(string method, string path, string arrives)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.SendAsync(Request(method, path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal(method, echo.Method);
        Assert.Equal(arrives, echo.Path);
        Assert.Equal("Bearer " + StubService.UserToken, echo.Authorization);
        Assert.Equal("", echo.Cookie);
        Assert.Equal(0, gateway.TokenRequests);
    }

    // Neither nobody nor a guest session of the smoke test gets to anyone's documents.
    [Theory]
    [InlineData("GET", "/api/content/documents")]
    [InlineData("POST", "/api/content/documents")]
    [InlineData("GET", "/api/knowledge/topics")]
    public async Task WithoutLogin_IsRefused_AndNothingIsForwarded(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var nobody = web.CreateBrowser();
        using var guest = web.CreateBrowser();
        using var session = await guest.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        using var withoutSession = await nobody.SendAsync(Request(method, path), TestContext.Current.CancellationToken);
        using var asGuest = await guest.SendAsync(Request(method, path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    // Only what the page needs goes on: no single document, no change of a topic, no other path of the services.
    [Theory]
    [InlineData("DELETE", "/api/content/documents")]
    [InlineData("PUT", "/api/content/documents")]
    [InlineData("GET", "/api/content/documents/0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01")]
    [InlineData("POST", "/api/knowledge/topics")]
    [InlineData("PATCH", "/api/knowledge/topics/0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01")]
    [InlineData("GET", "/api/knowledge/topics/0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01")]
    [InlineData("GET", "/api/content/sections")]
    public async Task OtherMethodsAndPaths_AreNotForwarded(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var before = gateway.Requests;

        using var response = await browser.SendAsync(Request(method, path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, gateway.Requests);
    }

    // A signed-in user's page asks every few seconds while a text is analyzed, a whole class may sit behind one address.
    // The limit only holds back what goes out with the technical token of a guest session.
    [Fact]
    public async Task CallsOfASignedInUser_AreNotLimited()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder
            .UseSetting("BackendToken:PerClientPerMinute", "1")
            .UseSetting("BackendToken:TotalPerMinute", "1"));
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        for (var i = 0; i < 5; i++)
        {
            using var documents = await browser.GetAsync("/api/content/documents", TestContext.Current.CancellationToken);
            using var ping = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, documents.StatusCode);
            Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        }
    }

    [Fact]
    public async Task CallsOfAGuestSession_StayLimited_NextToASignedInUser()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder
            .UseSetting("BackendToken:PerClientPerMinute", "1")
            .UseSetting("BackendToken:TotalPerMinute", "100"));
        using var user = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(user);
        using var guest = web.CreateBrowser();
        using var session = await guest.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        using var userCall = await user.GetAsync("/api/content/documents", TestContext.Current.CancellationToken);
        using var first = await guest.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);
        using var second = await guest.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, userCall.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    // The longest text Content takes gets through the web host whole.
    [Fact]
    public async Task LongestText_GetsThrough()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var body = $$"""{"title":null,"text":"{{new string('ü', 200_000)}}"}""";

        using var response = await browser.PostAsync(
            "/api/content/documents", new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { text = "Text" });
        }

        return request;
    }
}