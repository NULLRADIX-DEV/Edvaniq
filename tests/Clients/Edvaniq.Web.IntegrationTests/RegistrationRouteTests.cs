using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;

namespace Edvaniq.Web.IntegrationTests;

// #249: registering goes through the web host without a session and without any token: Identity's endpoint is open,
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