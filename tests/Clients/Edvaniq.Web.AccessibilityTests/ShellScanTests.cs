using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// axe-core scans every page of the shell on a phone and on a desktop, with the menu open and with the backend gone. A
// single violation fails the test and the message names rule, impact, element and the page that explains the fix.
public sealed class ShellScanTests(WebShell shell)
{
    private const string NotFound = "/there-is-no-such-page";

    public static TheoryData<string, int> Pages { get; } = Combine(["/", "/systemstatus", "/datenschutz", "/open-source", NotFound]);

    public static TheoryData<string, int> SystemStatus { get; } = Combine(["/systemstatus"]);

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_HasNoViolations(string path, int width)
    {
        await using var page = await shell.OpenAsync(shell.Address, path, width);
        await WaitForContentAsync(page.Page, path);

        await AxeScan.AssertNoViolationsAsync(page.Page, $"{path} at {width} px");
    }

    [Fact]
    public async Task OpenMenu_HasNoViolations()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/", WebShell.PhoneWidth);

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Menü" }).ClickAsync();
        await Assertions.Expect(page.Page.Locator("#shell-nav")).ToBeVisibleAsync();

        await AxeScan.AssertNoViolationsAsync(page.Page, $"/ at {WebShell.PhoneWidth} px with the menu open");
    }

    // The warning when the server does not answer and the failed ping are the only places with the red pen colors. The
    // ping fails only after the web host has given up waiting for a token, which takes several seconds.
    [Theory]
    [MemberData(nameof(SystemStatus))]
    public async Task PageWithoutBackend_HasNoViolations(string path, int width)
    {
        await using var page = await shell.OpenAsync(shell.AddressWithoutGateway, path, width);
        await Assertions.Expect(page.Page.Locator(".backend-banner")).ToBeVisibleAsync();
        await Assertions.Expect(page.Page.Locator(".ping-failed")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        await AxeScan.AssertNoViolationsAsync(page.Page, $"{path} without a backend at {width} px");
    }

    // The status page shows the ping only once it has come back.
    private static async Task WaitForContentAsync(IPage page, string path)
    {
        if (path == "/systemstatus")
        {
            await Assertions.Expect(page.Locator(".ping-result")).ToContainTextAsync("Alles in Ordnung");
        }
    }

    private static TheoryData<string, int> Combine(string[] paths)
    {
        var data = new TheoryData<string, int>();
        foreach (var path in paths)
        {
            data.Add(path, WebShell.PhoneWidth);
            data.Add(path, WebShell.DesktopWidth);
        }

        return data;
    }
}