using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// axe-core scans every page of the shell on a phone and on a desktop, with the menu open, signed in and with the
// backend gone. A single violation fails the test and the message names rule, impact, element and the page that
// explains the fix.
public sealed class ShellScanTests(WebShell shell)
{
    private const string NotFound = "/there-is-no-such-page";

    public static TheoryData<string, int> Pages { get; } = Combine(["/", "/login", "/datenschutz", "/open-source", "/bausteine", NotFound]);

    public static TheoryData<string, int> SignedInPages { get; } = Combine(["/", "/lernziele", "/systemstatus", "/themen", "/themen/verzeichnis"]);

    public static TheoryData<string, int> Login { get; } = Combine(["/login"]);

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_HasNoViolations(string path, int width)
    {
        await using var page = await shell.OpenAsync(shell.Address, path, width);

        await AxeScan.AssertNoViolationsAsync(page.Page, $"{path} at {width} px");
    }

    [Theory]
    [MemberData(nameof(SignedInPages))]
    public async Task SignedInPage_HasNoViolations(string path, int width)
    {
        await using var page = await shell.OpenSignedInAsync(path, width);
        await WaitForContentAsync(page.Page, path);

        await AxeScan.AssertNoViolationsAsync(page.Page, $"{path} signed in at {width} px");
    }

    [Fact]
    public async Task OpenMenu_HasNoViolations()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/", WebShell.PhoneWidth);

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Menü" }).ClickAsync();
        await Assertions.Expect(page.Page.Locator("#shell-nav")).ToBeVisibleAsync();

        await AxeScan.AssertNoViolationsAsync(page.Page, $"/ at {WebShell.PhoneWidth} px with the menu open");
    }

    // The warning when the server does not answer and a failed login are in the red pen colors. The login fails only
    // after the web host has given up on Identity, which takes a few seconds.
    [Theory]
    [MemberData(nameof(Login))]
    public async Task LoginWithoutBackend_HasNoViolations(string path, int width)
    {
        await using var page = await shell.OpenAsync(shell.AddressWithoutGateway, path, width);
        await Assertions.Expect(page.Page.Locator(".backend-banner")).ToBeVisibleAsync();

        await page.Page.GetByLabel("Name").FillAsync("ben");
        await page.Page.GetByLabel("Passwort").FillAsync("richtig-langes-passwort");
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Anmelden" }).ClickAsync();
        await Assertions.Expect(page.Page.Locator(".login-hint")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        await AxeScan.AssertNoViolationsAsync(page.Page, $"{path} with a failed login without a backend at {width} px");
    }

    // The status page shows the ping only once it has come back, the topics page its topics.
    private static async Task WaitForContentAsync(IPage page, string path)
    {
        if (path == "/systemstatus")
        {
            await Assertions.Expect(page.Locator(".ping-result")).ToContainTextAsync("Alles in Ordnung");
        }

        if (path == "/")
        {
            await Assertions.Expect(page.Locator(".plan-blocks")).ToContainTextAsync("Photosynthese");
        }

        if (path == "/lernziele")
        {
            await Assertions.Expect(page.Locator(".goal-card")).ToHaveCountAsync(2);
        }

        if (path == "/themen/verzeichnis")
        {
            await Assertions.Expect(page.Locator(".directory-list")).ToContainTextAsync("Photosynthese");
        }

        if (path == "/themen")
        {
            await Assertions.Expect(page.Locator(".topic-list")).ToContainTextAsync("Photosynthese");
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