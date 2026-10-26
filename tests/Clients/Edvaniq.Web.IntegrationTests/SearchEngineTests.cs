using System.Net;

namespace Edvaniq.Web.IntegrationTests;

// Until the start with real users, no page of the shell belongs in a search engine. The header X-Robots-Tag on every
// answer says so (SecurityHeaderTests), the page repeats it for tools that only read the HTML.
public sealed class SearchEngineTests
{
    [Theory]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/datenschutz", HttpStatusCode.OK)]
    [InlineData("/gibt-es-nicht", HttpStatusCode.NotFound)]
    public async Task EveryPage_AsksNotToBeIndexed(string path, HttpStatusCode status)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\"", html);
    }
}