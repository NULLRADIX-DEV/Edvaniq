using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #379: the page /plan reads the week through one route of the web host: only signed in, only GET, with the user's
// token instead of the browser's cookie.
public sealed class WeekPlanRouteTests
{
    private const string Path = "/api/planning/plan/week";

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
        Assert.Equal("/planning/plan/week", echo.Path);
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

        using var withoutSession = await nobody.GetAsync(Path, TestContext.Current.CancellationToken);
        using var asGuest = await guest.GetAsync(Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    [Theory]
    [InlineData("POST", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("GET", $"{Path}/more")]
    public async Task OtherMethodsAndPaths_AreNotForwarded(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var before = gateway.Requests;

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, gateway.Requests);
    }
}