using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Edvaniq.Web.IntegrationTests;

// On the server the console is the log. Every line carries the scopes, among them the TraceId of its request, so one
// id from a problem finds the lines of web host, gateway and services. ServiceDefaults sets this for every process,
// the web host stands in for them here. What lets someone in never gets into it: the client secret of the web host,
// the token it gets from Identity and the session cookie of the browser.
public sealed class LoggingTests
{
    [Fact]
    public void OutsideDevelopment_EveryLineCarriesTheTraceId()
    {
        using var web = new WebFactory("http://127.0.0.1:9", configure: builder => builder.UseEnvironment("Production"));

        var console = web.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().CurrentValue;
        var factory = web.Services.GetRequiredService<IOptions<LoggerFactoryOptions>>().Value;

        Assert.True(console.IncludeScopes);
        Assert.True(console.SingleLine);
        Assert.True(factory.ActivityTrackingOptions.HasFlag(ActivityTrackingOptions.TraceId));
    }

    [Fact]
    public async Task Ping_LogsNoSecret()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address, configure: builder => builder.UseEnvironment("Production"));
        using var browser = web.CreateBrowser(handleCookies: false);
        var session = await StartSessionAsync(browser);

        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The log saw the token request, so it had the chance to show the secret.
        Assert.NotEmpty(web.Logs.Containing(StubService.TokenPath));
        Assert.Empty(web.Logs.Containing(web.ClientSecret, "stub-token-1", session));
    }

    // Without a token the browser gets a problem. The warning names its trace and why, still without a secret.
    [Fact]
    public async Task NoToken_TheWarningNamesTheTraceOfTheProblem_AndNoSecret()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.TokenStatus = StatusCodes.Status400BadRequest;
        await using var web = new WebFactory(gateway.Address, configure: builder => builder.UseEnvironment("Production"));
        using var browser = web.CreateBrowser(handleCookies: false);
        var session = await StartSessionAsync(browser);

        using var response = await browser.PostAsync("/api/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var warning = Assert.Single(web.Logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(problem.GetProperty("traceId").GetString(), warning.TraceId);
        Assert.Contains("/api/planning/ping", warning.Message);
        Assert.Contains("400", warning.Text);
        Assert.Empty(web.Logs.Containing(web.ClientSecret, session));
    }

    // The browser sends its session cookie by hand, so the test knows the value the log must not show.
    private static async Task<string> StartSessionAsync(HttpClient browser)
    {
        using var response = await browser.PostAsync("/session", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0];
        browser.DefaultRequestHeaders.Add("Cookie", cookie);
        return cookie.Split('=', 2)[1];
    }
}