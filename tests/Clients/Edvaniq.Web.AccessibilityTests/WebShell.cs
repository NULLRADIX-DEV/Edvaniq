using System.Net;

using Edvaniq.Testing;
using Edvaniq.Web.IntegrationTests;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(Edvaniq.Web.AccessibilityTests.WebShell))]

namespace Edvaniq.Web.AccessibilityTests;

// The web shell as a visitor gets it: the real web host on Kestrel serves the WebAssembly client and Chromium runs it.
// The gateway behind the host is a stub, once one that answers like the real one and once an address nobody listens on.
// Started once for all tests.
public sealed class WebShell : IAsyncLifetime
{
    public const int PhoneWidth = 375;
    public const int DesktopWidth = 1280;

    private StubService? gateway;
    private WebFactory? healthyHost;
    private WebFactory? downHost;
    private IPlaywright? playwright;
    private IBrowser? browser;

    // The web host with a gateway that answers, and the one whose gateway is gone.
    public string Address { get; private set; } = "";

    public string AddressWithoutGateway { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        gateway = await StubService.StartAsync("gateway-stub");
        gateway.Answer("POST", "/planning/ping", new { id = 42, pingedAt = new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero) });

        (healthyHost, Address) = Start(gateway.Address);
        (downHost, AddressWithoutGateway) = Start("http://127.0.0.1:9");

        // A start on a slow runner takes its time.
        Assertions.SetDefaultExpectTimeout(15_000);
        playwright = await Playwright.CreateAsync();
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Timeout = 60_000 });
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Chromium is not installed for Playwright. Install it once with: pwsh "
                + Path.Combine(AppContext.BaseDirectory, "playwright.ps1") + " install --only-shell chromium",
                exception);
        }
    }

    public async Task<ShellPage> OpenAsync(string address, string path, int width)
    {
        var page = await NewPageAsync(width);
        try
        {
            await page.LoadAsync(address + path);
            return page;
        }
        catch
        {
            await page.DisposeAsync();
            throw;
        }
    }

    // A page that does not belong to the shell, to try the scan on.
    public async Task<ShellPage> OpenHtmlAsync(string html, int width = DesktopWidth)
    {
        var page = await NewPageAsync(width);
        await page.Page.SetContentAsync(html);
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        playwright?.Dispose();

        if (healthyHost is not null)
        {
            await healthyHost.DisposeAsync();
        }

        if (downHost is not null)
        {
            await downHost.DisposeAsync();
        }

        if (gateway is not null)
        {
            await gateway.DisposeAsync();
        }
    }

    // Every page gets a context of its own, so no cookie or cache carries over from one test to the next.
    private async Task<ShellPage> NewPageAsync(int width)
    {
        var context = await browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 800 },
            Locale = "de-DE",
        });
        return new ShellPage(context, await context.NewPageAsync());
    }

    // The host logs every retry against the missing gateway with a stack trace, which only buries the test results.
    private static (WebFactory Host, string Address) Start(string gatewayAddress)
    {
        var host = new WebFactory(gatewayAddress, configure: builder =>
        {
            builder.UseSetting("Logging:LogLevel:Default", "None");
            builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "None");
        });
        host.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        host.StartServer();
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return (host, address);
    }
}