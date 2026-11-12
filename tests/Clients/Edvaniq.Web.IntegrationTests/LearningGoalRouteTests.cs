using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #273: the assistant lists and creates learning goals through one route of the web host: only signed in, only GET and
// POST. The backend sees the user's token, never the browser's cookie.
public sealed class LearningGoalRouteTests
{
    private const string Path = "/api/planning/learning-goals";

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task AfterLogin_TheCallReachesPlanning_WithTheUsersToken(string method)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var request = new HttpRequestMessage(new HttpMethod(method), Path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { kind = "FreeLearning", name = "C#" });
        }

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal(method, echo.Method);
        Assert.Equal("/planning/learning-goals", echo.Path);
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

        using var withoutSession = await nobody.PostAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);
        using var asGuest = await guest.PostAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    // The default goal is Content's business, a goal by id and changes come later.
    [Theory]
    [InlineData("PUT", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("POST", $"{Path}/default")]
    [InlineData("GET", $"{Path}/5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50")]
    [InlineData("GET", "/api/planning/learning")]
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