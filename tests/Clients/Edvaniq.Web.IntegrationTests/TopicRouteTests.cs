using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #317 and #295: adding, renaming, hiding and showing topics go through the web host: only signed in, each with exactly
// its method. The backend sees the user's token, never the browser's cookie.
public sealed class TopicRouteTests
{
    private const string Path = "/api/knowledge/topics";
    private const string Topic = $"{Path}/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";

    [Theory]
    [InlineData("POST", Path, "/knowledge/topics")]
    [InlineData("PATCH", Topic, "/knowledge/topics/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50")]
    [InlineData("PUT", $"{Topic}/hidden", "/knowledge/topics/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50/hidden")]
    [InlineData("DELETE", $"{Topic}/hidden", "/knowledge/topics/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50/hidden")]
    public async Task AfterLogin_TheChangeReachesKnowledge_WithTheUsersToken(string method, string path, string arrives)
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
    }

    [Theory]
    [InlineData("POST", Path)]
    [InlineData("PATCH", Topic)]
    [InlineData("PUT", $"{Topic}/hidden")]
    [InlineData("DELETE", $"{Topic}/hidden")]
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

    [Theory]
    [InlineData("PUT", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("POST", Topic)]
    [InlineData("PUT", Topic)]
    [InlineData("DELETE", Topic)]
    [InlineData("PATCH", $"{Topic}/hidden")]
    [InlineData("POST", $"{Topic}/hidden")]
    [InlineData("GET", $"{Topic}/hidden")]
    [InlineData("PATCH", $"{Path}/not-a-guid")]
    [InlineData("PUT", $"{Path}/not-a-guid/hidden")]
    [InlineData("PATCH", $"{Topic}/terms")]
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

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PATCH")
        {
            request.Content = JsonContent.Create(new { name = "Zellatmung", learningGoalId = Guid.NewGuid() });
        }

        return request;
    }
}