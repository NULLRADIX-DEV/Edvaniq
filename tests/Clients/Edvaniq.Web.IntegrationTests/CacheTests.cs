using System.Net;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// The server is the only source of the user's data: the browser keeps nothing it got from /api or /session, so a page
// that asks again gets the server's state. The files of the shell stay cacheable.
public sealed class CacheTests
{
    [Theory]
    [InlineData("GET", "/api/content/documents")]
    [InlineData("GET", "/api/knowledge/topics")]
    [InlineData("GET", "/session/user")]
    [InlineData("GET", "/api/unbekannt")]
    public async Task UserData_IsNeverStored(string method, string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, $"{path} answered Cache-Control: {response.Headers.CacheControl}");
    }

    [Fact]
    public async Task WithoutLogin_TheRefusal_IsNotStoredEither()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/api/content/documents", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_content/Edvaniq.Client.DesignSystem/edvaniq.css")]
    [InlineData("/_content/Edvaniq.Client.UI/return-signal.js")]
    public async Task TheShellsFiles_StayCacheable(string path)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.CacheControl?.NoStore ?? false, $"{path} answered Cache-Control: {response.Headers.CacheControl}");
    }
}