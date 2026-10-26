using System.Net;

namespace Edvaniq.Web.IntegrationTests;

// The privacy notice belongs to every visitor: the page answers without a session and arrives prerendered.
public sealed class PrivacyPageTests
{
    [Fact]
    public async Task PrivacyNotice_NeedsNoSession()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await browser.GetAsync("/datenschutz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(">Datenschutzhinweis</h1>", html);
        Assert.Contains("Vorläufige Fassung", html);
    }
}