using System.Net;
using System.Net.Http.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// The practice page reaches Assessment only through two routes of the web host: asking a question of a topic and
// answering it. Only signed in, only POST, and the attempt only as a Guid. The backend sees the user's token, never the
// browser's cookie.
public sealed class QuizRouteTests
{
    private const string Attempt = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";

    [Theory]
    [InlineData("/api/assessment/attempts", "/assessment/attempts")]
    [InlineData($"/api/assessment/attempts/{Attempt}/answer", $"/assessment/attempts/{Attempt}/answer")]
    public async Task AfterLogin_TheCallReachesAssessment_WithTheUsersToken(string path, string arrives)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.PostAsync(path, Body(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal("POST", echo.Method);
        Assert.Equal(arrives, echo.Path);
        Assert.Equal("Bearer " + StubService.UserToken, echo.Authorization);
        Assert.Equal("", echo.Cookie);
        Assert.Equal(0, gateway.TokenRequests);
    }

    [Theory]
    [InlineData("/api/assessment/attempts")]
    [InlineData($"/api/assessment/attempts/{Attempt}/answer")]
    public async Task WithoutLogin_IsRefused_AndNothingIsForwarded(string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var nobody = web.CreateBrowser();
        using var guest = web.CreateBrowser();
        using var session = await guest.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        using var withoutSession = await nobody.PostAsync(path, Body(), TestContext.Current.CancellationToken);
        using var asGuest = await guest.PostAsync(path, Body(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asGuest.StatusCode);
        Assert.Equal(0, gateway.Requests);
    }

    // Nothing else of Assessment goes on: no reading of attempts, no other path, nothing that only looks like an attempt.
    [Theory]
    [InlineData("GET", "/api/assessment/attempts")]
    [InlineData("PUT", "/api/assessment/attempts")]
    [InlineData("GET", $"/api/assessment/attempts/{Attempt}")]
    [InlineData("POST", $"/api/assessment/attempts/{Attempt}")]
    [InlineData("GET", $"/api/assessment/attempts/{Attempt}/answer")]
    [InlineData("PUT", $"/api/assessment/attempts/{Attempt}/answer")]
    [InlineData("POST", "/api/assessment/attempts/not-a-guid/answer")]
    [InlineData("POST", "/api/assessment/attempts/1/answer")]
    [InlineData("POST", $"/api/assessment/attempts/{Attempt}/answer/more")]
    [InlineData("POST", $"/api/assessment/attempts/{Attempt}/x/answer")]
    [InlineData("POST", $"/api/assessment/attempts/{Attempt}%2Fx/answer")]
    [InlineData("POST", $"/api/assessment/attempts/x%2F{Attempt}/answer")]
    [InlineData("POST", $"/api/assessment/attempts/{Attempt}%2F..%2F..%2Ftopics/answer")]
    [InlineData("POST", "/api/assessment/attempts/answer")]
    [InlineData("POST", "/api/assessment/topics")]
    [InlineData("GET", "/api/assessment/topics")]
    [InlineData("POST", "/api/assessment/questions")]
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
            request.Content = Body();
        }

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, gateway.Requests);
    }

    private static JsonContent Body() => JsonContent.Create(new { option = 0, responseTimeMs = 1000 });
}