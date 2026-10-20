using System.Net;

namespace Edvaniq.Web.IntegrationTests;

// The open source notices belong to every visitor: the page answers without a session and arrives prerendered.
public sealed class OpenSourcePageTests
{
    [Fact]
    public async Task OpenSourceNotices_NeedNoSession()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await browser.GetAsync("/open-source", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(">Open-Source-Hinweise</h1>", html);
        Assert.Contains(">NuGet-Pakete</caption>", html);
    }
}