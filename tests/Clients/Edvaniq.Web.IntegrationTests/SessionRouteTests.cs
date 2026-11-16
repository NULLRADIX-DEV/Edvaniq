using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #396: the runner starts a session, moves it on and ends it through the web host. Only signed in, only POST, with the
// user's token instead of the browser's cookie.
public sealed class SessionRouteTests
{
    private const string Id = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";

    public static TheoryData<string> Forwarded => new()
    {
        "/api/planning/sessions",
        "/api/planning/sessions/bonus",
        $"/api/planning/sessions/{Id}/advance",
        $"/api/planning/sessions/{Id}/complete",
        $"/api/planning/sessions/{Id}/abort",
    };

    [Theory]
    [MemberData(nameof(Forwarded))]
    public async Task AfterLogin_TheCallReachesPlanning_WithTheUsersToken(string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal("POST", echo.Method);
        Assert.Equal(path["/api".Length..], echo.Path);
        Assert.Equal("Bearer " + StubService.UserToken, echo.Authorization);
        Assert.Equal("", echo.Cookie);
    }

    [Theory]
    [MemberData(nameof(Forwarded))]
    public async Task WithoutLogin_IsRefused_AndNothingIsForwarded(string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var nobody = web.CreateBrowser();
        using var guest = web.CreateBrowser();
        using var session = await guest.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        using var withoutSession = await nobody.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);
        using var asGuest = await guest.PostAsJsonAsync(path, new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    // No reading of sessions and no activity yet (#397, #399), no other step and no id that isn't one.
    [Theory]
    [InlineData("GET", "/api/planning/sessions")]
    [InlineData("GET", "/api/planning/sessions/bonus")]
    [InlineData("GET", "/api/planning/sessions/active")]
    [InlineData("GET", $"/api/planning/sessions/{Id}")]
    [InlineData("GET", $"/api/planning/sessions/{Id}/advance")]
    [InlineData("DELETE", $"/api/planning/sessions/{Id}/abort")]
    [InlineData("POST", "/api/planning/sessions/active")]
    [InlineData("POST", "/api/planning/sessions/bonuses")]
    [InlineData("POST", $"/api/planning/sessions/{Id}")]
    [InlineData("POST", $"/api/planning/sessions/{Id}/activity")]
    [InlineData("POST", $"/api/planning/sessions/{Id}/advanced")]
    [InlineData("POST", $"/api/planning/sessions/{Id}/abort/more")]
    [InlineData("POST", "/api/planning/sessions/not-a-guid/complete")]
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