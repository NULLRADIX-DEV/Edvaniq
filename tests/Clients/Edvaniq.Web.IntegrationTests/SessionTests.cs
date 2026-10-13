using System.Net;
using System.Text.RegularExpressions;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// The only thing the browser holds is the session cookie. Scripts cannot read it, it travels only over HTTPS and only
// to this site.
public sealed partial class SessionTests
{
    [Fact]
    public async Task StartingASession_SetsAProtectedCookie()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        Assert.StartsWith("__host-edvaniq-session=", cookie);
        Assert.Contains("; secure", cookie);
        Assert.Contains("; httponly", cookie);
        Assert.Contains("; samesite=strict", cookie);
        Assert.Contains("; path=/", cookie);
        Assert.DoesNotContain("domain=", cookie);
    }

    [Fact]
    public async Task Session_ExistsFromStartUntilEnd()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(browser, HttpMethod.Get));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(browser, HttpMethod.Post));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(browser, HttpMethod.Get));
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(browser, HttpMethod.Delete));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(browser, HttpMethod.Get));
    }

    // The cookie is encrypted by the web host. No token in readable form reaches the browser, neither in the cookie
    // nor in the page.
    [Fact]
    public async Task TheBrowserGetsNoToken()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser(handleCookies: false);

        using var session = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken);
        var page = await browser.GetStringAsync("/", TestContext.Current.CancellationToken);

        var cookie = Assert.Single(session.Headers.GetValues("Set-Cookie"));
        Assert.DoesNotMatch(Jwt(), cookie);
        Assert.DoesNotMatch(Jwt(), page);
        Assert.DoesNotContain("Bearer", page);
    }

    // Header, payload and signature, the first two base64url JSON. Blazor's own state in the page is base64 JSON too,
    // so "eyJ" alone would not tell a token apart.
    [GeneratedRegex(@"eyJ[\w-]+\.eyJ[\w-]+\.[\w-]+")]
    private static partial Regex Jwt();

    private static async Task<HttpStatusCode> StatusAsync(HttpClient browser, HttpMethod method)
    {
        using var request = new HttpRequestMessage(method, "/session");
        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        // A 401 stays an answer for the client in the browser, not the HTML not-found page.
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        return response.StatusCode;
    }
}