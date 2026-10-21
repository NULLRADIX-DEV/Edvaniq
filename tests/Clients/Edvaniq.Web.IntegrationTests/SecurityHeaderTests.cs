using System.Net;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Web.IntegrationTests;

// The minimum list of headers that every answer of the web host carries, whatever the answer is. The shell must keep
// loading with them.
public sealed class SecurityHeaderTests
{
    private static readonly (string Name, string Value)[] Expected =
    [
        ("Content-Security-Policy", "frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'"),
        ("X-Frame-Options", "DENY"),
        ("X-Content-Type-Options", "nosniff"),
        ("Referrer-Policy", "strict-origin-when-cross-origin"),
        ("Cross-Origin-Opener-Policy", "same-origin"),
        ("Permissions-Policy", "camera=(), microphone=(), geolocation=()"),
    ];

    [Theory]
    [InlineData("GET", "/", HttpStatusCode.OK)]
    [InlineData("GET", "/gibt-es-nicht", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/status", HttpStatusCode.OK)]
    [InlineData("GET", "/api/unbekannt", HttpStatusCode.NotFound)]
    [InlineData("GET", "/session", HttpStatusCode.Unauthorized)]
    [InlineData("POST", "/api/planning/ping", HttpStatusCode.Unauthorized)]
    public async Task EveryAnswer_CarriesTheHeaders(string method, string path, HttpStatusCode status)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        AssertHeaders(response);
    }

    [Fact]
    public async Task ARefusedRequest_CarriesTheHeadersToo()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/session");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        using var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertHeaders(response);
    }

    // The shell is a page and its files, the framework's script among them, and none of it is blocked by the headers.
    [Theory]
    [InlineData("/")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_content/Edvaniq.Client.DesignSystem/edvaniq.css")]
    public async Task TheShell_Loads(string path)
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHeaders(response);
    }

    // The header of the antiforgery check would be SAMEORIGIN, and a second value next to DENY.
    [Fact]
    public async Task Antiforgery_SendsNoFrameOptionsOfItsOwn()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        using var scope = web.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);

        Assert.False(context.Response.Headers.ContainsKey("X-Frame-Options"));
    }

    private static void AssertHeaders(HttpResponseMessage response)
    {
        foreach (var (name, value) in Expected)
        {
            Assert.Equal(value, Assert.Single(response.Headers.GetValues(name)));
        }
    }
}