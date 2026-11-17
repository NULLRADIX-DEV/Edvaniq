using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;

namespace Edvaniq.Web.IntegrationTests;

// #249, #250: registering and confirming go through the web host without a session and without any token: Identity's endpoint is open,
// the browser's cookie stays here. A client address may register only a few times an hour.
public sealed class RegistrationRouteTests
{
    private const string Path = "/api/identity/accounts";

    [Fact]
    public async Task Registration_ReachesIdentity_WithoutTokenOrCookie()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await browser.PostAsJsonAsync(Path, new { email = "lena@example.org" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal(("POST", "/identity/accounts", "", ""), (echo.Method, echo.Path, echo.Authorization, echo.Cookie));
        Assert.Equal(0, gateway.TokenRequests);
    }

    [Fact]
    public async Task TooManyRegistrations_FromOneAddress_AreRefusedWithRetryAfter()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder.UseSetting("Login:RegistrationsPerClientPerHour", "2"));
        using var browser = web.CreateBrowser();

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await browser.PostAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await browser.PostAsJsonAsync(Path, new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter?.Delta);
        Assert.Equal(2, gateway.Requests - gateway.TokenRequests);
    }

    // #250: confirming and asking for a new link go the same way, each with its own limit per client address.
    [Theory]
    [InlineData("/api/identity/accounts/confirmations", "/identity/accounts/confirmations", "Login:ConfirmationsPerClientPerMinute")]
    [InlineData("/api/identity/accounts/confirmations/resend", "/identity/accounts/confirmations/resend", "Login:ResendsPerClientPer10Minutes")]
    public async Task ConfirmationRoutes_ReachIdentity_WithoutToken_AndHaveTheirLimit(string path, string forwarded, string limit)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder.UseSetting(limit, "1"));
        using var browser = web.CreateBrowser();

        using var response = await browser.PostAsJsonAsync(path, new { token = "t" }, TestContext.Current.CancellationToken);
        using var refused = await browser.PostAsJsonAsync(path, new { token = "t" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal(("POST", forwarded, "", ""), (echo.Method, echo.Path, echo.Authorization, echo.Cookie));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(0, gateway.TokenRequests);
    }

    // #261: the versions of the legal texts to agree to, open like registering, with a limit per client address.
    [Fact]
    public async Task LegalVersions_ReachIdentity_WithoutToken_AndHaveTheirLimit()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder.UseSetting("Login:LegalVersionsPerClientPerMinute", "1"));
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/api/identity/legal-versions", TestContext.Current.CancellationToken);
        using var refused = await browser.GetAsync("/api/identity/legal-versions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal(("GET", "/identity/legal-versions", ""), (echo.Method, echo.Path, echo.Authorization));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Theory]
    [InlineData("GET", Path)]
    [InlineData("POST", $"{Path}/someone")]
    public async Task OtherMethodsAndPaths_AreNotForwarded(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }
}