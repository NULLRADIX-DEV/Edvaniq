using System.Net;

namespace Edvaniq.Web.IntegrationTests;

// The page sends its ping from the browser, so on a slow connection the first thing a visitor gets is the prerendered
// page. It already says that it is loading, and the runtime still takes it over.
public sealed class SystemStatusPageTests
{
    [Fact]
    public async Task SystemStatus_ArrivesPrerendered_WithTheLoadingHint()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await browser.GetAsync("/systemstatus", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(">Systemstatus</h1>", html);
        Assert.Matches("""<div role="status">\s*<p>Wird geladen …</p>""", html);
        Assert.Contains("<!--Blazor:", html);
    }
}