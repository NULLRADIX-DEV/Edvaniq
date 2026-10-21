using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Web.IntegrationTests;

// Outside Development an unexpected error ends on the error page of the shell: German, with the id of the request and a
// way to try again, and nothing about the exception. The host has no page that throws, so the tests add a path that does.
public sealed partial class ErrorPageTests
{
    private const string ExceptionText = "interner-fehlertext-4711";

    [Fact]
    public async Task UnexpectedError_ShowsThePlainErrorPage_WithTheTraceId_AndNoDetails()
    {
        var failed = new FailedRequest();
        await using var web = WebThatThrows(failed);
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/boom?seite=2", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(">Etwas ist schiefgelaufen</h1>", html);
        Assert.Contains("Erneut versuchen", html);
        Assert.Contains(">Zur Startseite</a>", html);
        Assert.DoesNotContain(ExceptionText, html);
        Assert.DoesNotContain(nameof(InvalidOperationException), html);
        Assert.DoesNotContain("Edvaniq.Web.IntegrationTests", html);
        Assert.DoesNotContain("Development", html);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", html);

        // The id on the page is the one of the request that failed, the one its log lines carry.
        Assert.Matches(TraceId(), failed.TraceId);
        Assert.Matches($"<code[^>]*>{failed.TraceId}</code>", html);
    }

    // The retry asks for the page that failed, with its query. It is a plain link, so it works without the runtime.
    [Fact]
    public async Task Retry_LeadsToThePageThatFailed()
    {
        await using var web = WebThatThrows(new FailedRequest());
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/boom?seite=2", TestContext.Current.CancellationToken);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Matches("""<a class="button" href="/boom\?seite=2" data-enhance-nav="false"[^>]*>Erneut versuchen</a>""", html);
    }

    // A path that starts with two slashes would lead to another site as a link. The retry stays on this one.
    [Fact]
    public async Task Retry_NeverLeavesTheSite()
    {
        await using var web = WebThatThrows(new FailedRequest());
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync(new Uri("https://localhost//fremde-seite.test/boom"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("href=\"/fremde-seite.test/boom\"", html);
        Assert.DoesNotContain("href=\"//", html);
    }

    // Once the runtime took the page over, its router would not know the error page and show "Seite nicht gefunden"
    // in its place. So the page has no component the runtime could take over. While an exception is handled, the
    // framework renders without interactivity anyway, this keeps it that way.
    [Fact]
    public async Task ErrorPage_AfterAnException_HasNothingTheRuntimeCouldTakeOver()
    {
        await using var web = WebThatThrows(new FailedRequest());
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/boom", TestContext.Current.CancellationToken);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<!--Blazor:", html);
        Assert.DoesNotContain("Seite nicht gefunden", html);
        Assert.Contains("""<a class="skip-link" """, html);
    }

    // Opening the page by hand does not break anything: it shows the same page, and the retry leads to the start page.
    // It is no page the runtime knows, so here it takes care that the page stays on the server.
    [Fact]
    public async Task ErrorPage_OpenedByHand_LeadsToTheStartPage_AndStaysOnTheServer()
    {
        await using var web = WebThatThrows(new FailedRequest());
        using var browser = web.CreateBrowser();

        using var response = await browser.GetAsync("/Error", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(">Etwas ist schiefgelaufen</h1>", html);
        Assert.Matches("""<a class="button" href="/" """, html);
        Assert.DoesNotContain("<!--Blazor:", html);
    }

    private static WebFactory WebThatThrows(FailedRequest failed) =>
        new("http://127.0.0.1:9", configure: builder => builder
            .UseEnvironment("Production")
            .ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new ThrowingPath(failed))));

    // Throws for every path that ends in /boom. It comes after the endpoints, so no page needs to exist for it, and it
    // writes down the trace of the request it fails.
    private sealed class ThrowingPath(FailedRequest failed) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, pass) =>
            {
                if (!context.Request.Path.Value!.EndsWith("/boom", StringComparison.Ordinal))
                {
                    return pass(context);
                }

                failed.TraceId = Activity.Current?.TraceId.ToHexString();
                throw new InvalidOperationException(ExceptionText);
            });
        };
    }

    private sealed class FailedRequest
    {
        public string? TraceId { get; set; }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceId();
}