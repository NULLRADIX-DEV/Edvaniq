using System.Net;
using System.Text.RegularExpressions;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Web.IntegrationTests;

// The minimum list of headers that every answer of the web host carries, whatever the answer is. The shell must keep
// loading with them.
public sealed class SecurityHeaderTests
{
    private const string FilePolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'none'";

    private const string ShellPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'nonce-{nonce}'; style-src 'self'; img-src 'self'; font-src 'self'; "
        + "connect-src 'self'; frame-src 'none'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'";

    // 16 random bytes in base64.
    private static readonly Regex ShellPolicyPattern =
        new("^" + Regex.Escape(ShellPolicy).Replace(Regex.Escape("{nonce}"), "(?<nonce>[A-Za-z0-9_-]{22})", StringComparison.Ordinal) + "$");

    private static readonly (string Name, string Value)[] Expected =
    [
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
        await using var web = Web(gateway.Address);
        using var browser = web.CreateBrowser();

        using var response = await browser.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        AssertHeaders(response);
    }

    [Fact]
    public async Task ARefusedRequest_CarriesTheHeadersToo()
    {
        await using var web = Web("http://127.0.0.1:9");
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
        await using var web = Web("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHeaders(response);
    }

    // The header of the antiforgery check would be SAMEORIGIN, and a second value next to DENY.
    [Fact]
    public async Task Antiforgery_SendsNoFrameOptionsOfItsOwn()
    {
        await using var web = Web("http://127.0.0.1:9");
        using var scope = web.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);

        Assert.False(context.Response.Headers.ContainsKey("X-Frame-Options"));
    }

    // #350: a user's file goes out under a stricter policy of its own, every other answer keeps the shell's, one from the
    // services included.
    [Fact]
    public async Task AUsersFile_GetsTheStricterPolicyOfItsRoute()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = Web(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.GetAsync("/api/content/documents/0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01/file", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FilePolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        AssertHeaders(response, withShellPolicy: false);
    }

    [Theory]
    [InlineData("/api/content/sections/0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01/original")]
    [InlineData("/api/content/documents")]
    public async Task ARouteWithoutAPolicy_KeepsTheShells(string path)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = Web(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHeaders(response);
    }

    // #352: the one inline script of the page, its import map, runs with the nonce of the answer it came in. Every answer
    // has a nonce of its own, so one that leaked from an earlier page is no use.
    [Fact]
    public async Task ThePage_CarriesTheNonceOfItsPolicy_NewForEveryAnswer()
    {
        await using var web = Web("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var first = await browser.GetAsync("/", TestContext.Current.CancellationToken);
        using var second = await browser.GetAsync("/", TestContext.Current.CancellationToken);

        var nonce = NonceOf(first);
        var page = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var importMap = Assert.Single(Regex.Matches(page, "<script[^>]*type=\"importmap\"[^>]*>"));
        Assert.Contains($"nonce=\"{nonce}\"", importMap.Value, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, "<script(?![^>]*\\bsrc=)[^>]*>"));
        Assert.NotEqual(nonce, NonceOf(second));
    }

    // Razor encodes "+" and "/" in an attribute. A nonce of base64url without padding has neither, so the page carries
    // it as the header does. With plain base64 about every third answer broke this.
    [Fact]
    public async Task TheNonce_HasNothingHtmlEncodes()
    {
        await using var web = Web("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        for (var i = 0; i < 40; i++)
        {
            using var response = await browser.GetAsync("/", TestContext.Current.CancellationToken);
            var nonce = NonceOf(response);
            Assert.Matches("^[A-Za-z0-9_-]{22}$", nonce);
            var page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains($"nonce=\"{nonce}\"", page, StringComparison.Ordinal);
        }
    }

    // A page holds the nonce of its own answer. A copy from the cache would come back under a newer policy and lose its
    // import map.
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/gibt-es-nicht")]
    public async Task APage_IsNeverStored(string path)
    {
        await using var web = Web("http://127.0.0.1:9");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore, $"{path} answered Cache-Control: {response.Headers.CacheControl}");
    }

    // dotnet watch talks to the page over a web socket of its own. Only in Development, and nothing else gets looser.
    [Fact]
    public async Task InDevelopment_OnlyTheLocalWebSocketIsAdded()
    {
        const string Development = "connect-src 'self' ws://localhost:* wss://localhost:*;";
        await using var web = Web("http://127.0.0.1:9", "Development");
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/", TestContext.Current.CancellationToken);

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains(Development, policy, StringComparison.Ordinal);
        NonceOf(policy.Replace(Development, "connect-src 'self';", StringComparison.Ordinal));
    }

    // Tests of a web host are in Development unless they say otherwise. The policy that counts is the one outside. The
    // files of the shell come from the build output, as they do in Development.
    private static WebFactory Web(string gatewayAddress, string environment = "Production") =>
        new(gatewayAddress, configure: builder => builder.UseEnvironment(environment).UseStaticWebAssets());

    // A policy in the configuration that would allow more than the shell's never counts.
    [Fact]
    public async Task EveryPolicyOfARoute_IsStricterThanTheShells()
    {
        await using var web = Web("http://127.0.0.1:9");
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
    [InlineData("default-src 'none'; frame-ancestors *; base-uri 'self'; object-src 'none'; form-action 'self'", false)]
    [InlineData("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'", false)]
    [InlineData("DEFAULT-SRC 'none'; FRAME-ANCESTORS 'none'; base-uri 'none'; form-action 'none'; script-src 'none'", true)]

    // A fetch directive that is left out falls back to default-src, in the browser as here.
    [InlineData("frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'none'; script-src 'none'", false)]
    [InlineData("default-src *; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; script-src 'none'", false)]
    [InlineData("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; script-src 'self' 'unsafe-inline'", false)]
    [InlineData("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; style-src 'self' 'unsafe-inline'", false)]
    [InlineData("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; connect-src *", false)]
    public void IsStricter_OnlyForAPolicyThatForbidsAtLeastWhatTheShellDoes(string policy, bool stricter) =>
        Assert.Equal(stricter, SecurityHeaders.IsStricter(policy));

    // The policy in the code is the one the tests know, nothing in it got looser.
    [Fact]
    public void TheShellPolicy_IsTheOneTheTestsKnow() => Assert.Equal(ShellPolicy, SecurityHeaders.ShellPolicy);

    private static void AssertHeaders(HttpResponseMessage response, bool withShellPolicy = true)
    {
        if (withShellPolicy)
        {
            Assert.NotNull(NonceOf(response));
        }

        foreach (var (name, value) in Expected)
        {
            Assert.Equal(value, Assert.Single(response.Headers.GetValues(name)));
        }
    }

    private static string NonceOf(HttpResponseMessage response) =>
        NonceOf(Assert.Single(response.Headers.GetValues("Content-Security-Policy")));

    private static string NonceOf(string policy)
    {
        var match = ShellPolicyPattern.Match(policy);
        Assert.True(match.Success, $"Unexpected policy: {policy}");
        return match.Groups["nonce"].Value;
    }
}