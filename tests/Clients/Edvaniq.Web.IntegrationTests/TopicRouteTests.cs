using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #317: the assistant adds the topics the user entered through the web host: only signed in, only POST next to the GET
// of the list. The backend sees the user's token, never the browser's cookie.
public sealed class TopicRouteTests
{
    private const string Path = "/api/knowledge/topics";

    [Fact]
    public async Task AfterLogin_AddingReachesKnowledge_WithTheUsersToken()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.PostAsJsonAsync(
            Path, new { name = "Zellatmung", learningGoalId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal("POST", echo.Method);
        Assert.Equal("/knowledge/topics", echo.Path);
        Assert.Equal("Bearer " + StubService.UserToken, echo.Authorization);
        Assert.Equal("", echo.Cookie);
    }

    [Fact]
    public async Task WithoutLogin_IsRefused_AndNothingIsForwarded()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var nobody = web.CreateBrowser();
        using var guest = web.CreateBrowser();
        using var session = await guest.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        using var withoutSession = await nobody.PostAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);
        using var asGuest = await guest.PostAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    // Renaming and hiding come with #295.
    [Theory]
    [InlineData("PUT", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("PATCH", $"{Path}/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50")]
    [InlineData("PUT", $"{Path}/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50/hidden")]
    [InlineData("DELETE", $"{Path}/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50/hidden")]
    [InlineData("POST", $"{Path}/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50")]
    public async Task OtherMethodsAndPaths_AreNotForwarded(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var before = gateway.Requests;

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, gateway.Requests);
    }
}