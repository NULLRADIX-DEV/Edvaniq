using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #512: the settings page reads and sets the daily budget through one route of the web host: only signed in, only GET
// and PUT. The backend sees the user's token, never the browser's cookie.
public sealed class DailyBudgetRouteTests
{
    private const string Path = "/api/planning/daily-budget";

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task AfterLogin_TheCallReachesPlanning_WithTheUsersToken(string method)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var request = new HttpRequestMessage(new HttpMethod(method), Path);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { monday = 30 });
        }

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal(method, echo.Method);
        Assert.Equal("/planning/daily-budget", echo.Path);
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

        using var withoutSession = await nobody.PutAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);
        using var asGuest = await guest.PutAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    [Theory]
    [InlineData("POST", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("GET", $"{Path}/monday")]
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