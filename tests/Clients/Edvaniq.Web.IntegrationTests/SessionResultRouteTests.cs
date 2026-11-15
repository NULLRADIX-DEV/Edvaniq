using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #406: the result of a session comes through one route of the web host: only signed in, only GET with a session id,
// with the user's token instead of the browser's cookie.
public sealed class SessionResultRouteTests
{
    private const string Id = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";

    private const string Path = $"/api/planning/sessions/{Id}/result";

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
        Assert.Equal($"/planning/sessions/{Id}/result", echo.Path);
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

    // Nothing else of the sessions goes through: not starting, finishing or reading one, and no id that isn't one.
    [Theory]
    [InlineData("POST", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("GET", "/api/planning/sessions/not-a-guid/result")]
    [InlineData("GET", $"/api/planning/sessions/{Id}")]
    [InlineData("GET", "/api/planning/sessions/active")]
    [InlineData("POST", "/api/planning/sessions")]
    [InlineData("POST", $"/api/planning/sessions/{Id}/complete")]
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