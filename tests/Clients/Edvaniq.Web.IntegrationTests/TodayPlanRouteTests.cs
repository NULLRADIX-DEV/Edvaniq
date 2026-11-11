using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #212: Home reads today's plan through one route of the web host: only signed in, only GET. The backend sees the
// user's token, never the browser's cookie.
public sealed class TodayPlanRouteTests
{
    private const string Path = "/api/planning/plan/today";

    [Fact]
    public async Task AfterLogin_TheCallReachesPlanning_WithTheUsersToken()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.GetAsync(Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal("GET", echo.Method);
        Assert.Equal("/planning/plan/today", echo.Path);
        Assert.Equal("Bearer " + StubService.UserToken, echo.Authorization);
        Assert.Equal("", echo.Cookie);
        Assert.Equal(0, gateway.TokenRequests);
    }

    [Fact]
    public async Task WithoutLogin_IsRefused_AndNothingIsForwarded()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var nobody = web.CreateBrowser();
        using var guest = web.CreateBrowser();
        using var session = await guest.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        using var withoutSession = await nobody.GetAsync(Path, TestContext.Current.CancellationToken);
        using var asGuest = await guest.GetAsync(Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    [Theory]
    [InlineData("POST", Path)]
    [InlineData("PUT", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("GET", "/api/planning/plan")]
    [InlineData("GET", "/api/planning/plan/tomorrow")]
    [InlineData("GET", $"{Path}/more")]
    public async Task OtherMethodsAndPaths_AreNotForwarded(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var before = gateway.Requests;

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, gateway.Requests);
    }
}