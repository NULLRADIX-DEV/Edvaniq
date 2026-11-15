using System.Net;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Web.IntegrationTests;

// The minimum list of headers that every answer of the web host carries, whatever the answer is. The shell must keep
// loading with them.
public sealed class SecurityHeaderTests
{
    private const string FilePolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'none'";

    private static readonly (string Name, string Value)[] Expected =
    [
        ("Content-Security-Policy", "frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'"),
        ("X-Frame-Options", "DENY"),
        ("X-Content-Type-Options", "nosniff"),
        ("Referrer-Policy", "strict-origin-when-cross-origin"),
        ("Cross-Origin-Opener-Policy", "same-origin"),
        ("Permissions-Policy", "camera=(), microphone=(), geolocation=()"),
        ("X-Robots-Tag", "noindex, nofollow"),
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

    // #350: a user's file goes out under a stricter policy of its own, every other answer keeps the shell's, one from the
    // services included.
    [Theory]
    [InlineData("/api/content/documents/0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01/file", FilePolicy)]
    [InlineData("/api/content/sections/0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01/original", SecurityHeaders.ShellPolicy)]
    [InlineData("/api/content/documents", SecurityHeaders.ShellPolicy)]
    public async Task ARouteWithAPolicy_GetsIt_OthersKeepTheShells(string path, string policy)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(policy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        foreach (var (name, value) in Expected.Where(header => header.Name != "Content-Security-Policy"))
        {
            Assert.Equal(value, Assert.Single(response.Headers.GetValues(name)));
        }
    }

    // A policy in the configuration that would allow more than the shell's never counts.
    [Fact]
    public async Task EveryPolicyOfARoute_IsStricterThanTheShells()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");
        var policies = web.Services.GetRequiredService<IConfiguration>().GetSection("ReverseProxy:Routes").GetChildren()
            .Select(route => route[$"Metadata:{SecurityHeaders.MetadataKey}"])
            .OfType<string>()
            .ToList();

        Assert.Equal([FilePolicy], policies);
        Assert.All(policies, policy => Assert.True(SecurityHeaders.IsStricter(policy), policy));
    }

    [Theory]
    [InlineData(SecurityHeaders.ShellPolicy, true)]
    [InlineData(FilePolicy, true)]
    [InlineData("default-src 'none'", false)]
    [InlineData("frame-ancestors *; base-uri 'self'; object-src 'none'; form-action 'self'", false)]
    [InlineData("frame-ancestors 'none'; base-uri 'none'; object-src 'none'", false)]
    [InlineData("FRAME-ANCESTORS 'none'; base-uri 'none'; object-src 'none'; form-action 'none'; script-src 'none'", true)]
    public void IsStricter_OnlyForAPolicyThatForbidsAtLeastWhatTheShellDoes(string policy, bool stricter) =>
        Assert.Equal(stricter, SecurityHeaders.IsStricter(policy));

    private static void AssertHeaders(HttpResponseMessage response)
    {
        foreach (var (name, value) in Expected)
        {
            Assert.Equal(value, Assert.Single(response.Headers.GetValues(name)));
        }
    }
}