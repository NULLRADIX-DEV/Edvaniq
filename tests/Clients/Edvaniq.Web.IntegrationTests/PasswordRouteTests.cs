using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #254: the settings page changes the password through one route of the web host: only signed in, only POST. The
// gateway sees the user's token, never the browser's cookie.
public sealed class PasswordRouteTests
{
    private const string Path = "/api/identity/accounts/password";

    [Fact]
    public async Task AfterLogin_TheCallReachesIdentity_WithTheUsersToken_AndWithoutCookie()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var tokenRequests = gateway.TokenRequests;

        using var response = await browser.PostAsJsonAsync(Path, new { currentPassword = "a", newPassword = "b" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal("POST", echo.Method);
        Assert.Equal("/identity/accounts/password", echo.Path);
        Assert.Equal("Bearer " + StubService.UserToken, echo.Authorization);
        Assert.Equal("", echo.Cookie);
        Assert.Equal(tokenRequests, gateway.TokenRequests);
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

    [Theory]
    [InlineData("GET", Path)]
    [InlineData("PUT", Path)]
    [InlineData("DELETE", Path)]
    [InlineData("POST", $"{Path}/more")]
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
