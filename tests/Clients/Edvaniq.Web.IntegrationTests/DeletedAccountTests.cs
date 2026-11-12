using System.Net;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Http;

namespace Edvaniq.Web.IntegrationTests;

// #953: once the account is deleted, the backend turns its token away with 401. The web host then ends the login like a
// logout, so nothing gets through any more. Only the user's own token ends a login that way.
public sealed class DeletedAccountTests
{
    private const string Ping = "/api/planning/ping";

    [Fact]
    public async Task UserTokenTurnedAway_EndsTheLogin_AndNothingIsForwardedAfterwards()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);
        var cookie = await LoginTests.LoginCookieAsync(browser);
        gateway.Fail("POST", "/planning/ping", StatusCodes.Status401Unauthorized);

        using var rejected = await SendAsync(browser, HttpMethod.Post, Ping, cookie);
        var forwarded = gateway.Requests;
        using var user = await SendAsync(browser, HttpMethod.Get, "/session/user", cookie);
        using var again = await SendAsync(browser, HttpMethod.Post, Ping, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, user.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal(forwarded, gateway.Requests);
    }

    // A 403 is no deleted account.
    [Fact]
    public async Task UserTokenForbidden_KeepsTheLogin()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);
        var cookie = await LoginTests.LoginCookieAsync(browser);
        gateway.Fail("POST", "/planning/ping", StatusCodes.Status403Forbidden);

        using var forbidden = await SendAsync(browser, HttpMethod.Post, Ping, cookie);
        using var user = await SendAsync(browser, HttpMethod.Get, "/session/user", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, user.StatusCode);
    }

    // The technical token of a guest session belongs to no account, a 401 for it ends nothing.
    [Fact]
    public async Task TechnicalTokenTurnedAway_KeepsTheGuestSession()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        using (var session = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, session.StatusCode);
        }

        gateway.Fail("POST", "/planning/ping", StatusCodes.Status401Unauthorized);

        using var rejected = await browser.PostAsync(Ping, content: null, TestContext.Current.CancellationToken);
        using var still = await browser.GetAsync("/session", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, still.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient browser, HttpMethod method, string path, string cookie)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        return await browser.SendAsync(request, TestContext.Current.CancellationToken);
    }
}