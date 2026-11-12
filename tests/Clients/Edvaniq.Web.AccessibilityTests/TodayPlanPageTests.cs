using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #212 AC3: Home with today's plan reads on a phone, a tablet and a desktop: nothing scrolls sideways, also with a
// topic name that is one long word, and axe-core finds nothing. The same for an empty plan and for the hints.
public sealed class TodayPlanPageTests(WebShell shell)
{
    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.TabletWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task Plan_ReadsAtEveryWidth(int width)
    {
        await using var page = await shell.OpenSignedInAsync("/", width, prepare: browser =>
            Material.AnswerPlanAsync(browser, Material.Plan(masteryAvailable: false)));
        var blocks = page.Page.Locator(".plan-block");
        await Assertions.Expect(blocks).ToHaveCountAsync(3);
        await Assertions.Expect(blocks.Nth(0)).ToContainTextAsync("Photosynthese");
        await Assertions.Expect(blocks.Nth(0)).ToContainTextAsync("Wiederholen");
        await Assertions.Expect(blocks.Nth(0)).ToContainTextAsync("10 Minuten");
        await Assertions.Expect(blocks.Nth(2)).ToContainTextAsync(Material.LongWord);
        await Assertions.Expect(page.Page.Locator(".plan-mastery")).ToBeVisibleAsync();
        await Assertions.Expect(page.Page.Locator(".plan-shortened")).ToContainTextAsync(Material.LongWord);

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/ with a plan at {width} px");
    }

    [Theory]
    [InlineData("noTopics", "Text einfügen", WebShell.PhoneWidth)]
    [InlineData("questionsPending", "Zu deinen Themen", WebShell.PhoneWidth)]
    [InlineData("allMastered", "Neuen Text einfügen", WebShell.DesktopWidth)]
    public async Task EmptyPlan_LeadsToTheNextStep(string reason, string link, int width)
    {
        await using var page = await shell.OpenSignedInAsync("/", width, prepare: browser =>
            Material.AnswerPlanAsync(browser, Material.Plan(reason)));
        var next = page.Page.Locator(".plan .state-actions").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = link, Exact = true });
        await Assertions.Expect(next).ToBeVisibleAsync();

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/ with an empty plan ({reason}) at {width} px");

        await next.ClickAsync();
        await Assertions.Expect(page.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/themen$"));
    }

    // AC1 in the browser: back on the tab after an answer elsewhere, Home shows the new plan by itself.
    [Fact]
    public async Task Return_ShowsTheNewPlan()
    {
        await using var page = await shell.OpenSignedInAsync("/", WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerPlanAsync(browser, Material.Plan(), Material.Plan("allMastered")));
        await Assertions.Expect(page.Page.Locator(".plan-block")).ToHaveCountAsync(3);

        // The page listens only once its script has loaded, and returns within two seconds count once, so a slow start
        // gets another go.
        var status = page.Page.Locator(".plan-status");
        for (var attempt = 0; attempt < 10 && !(await status.TextContentAsync() ?? "").Contains("Für heute steht nichts an.", StringComparison.Ordinal); attempt++)
        {
            await page.Page.EvaluateAsync("() => { window.dispatchEvent(new Event('blur')); window.dispatchEvent(new Event('focus')); }");
            await page.Page.WaitForTimeoutAsync(2_500);
        }

        await Assertions.Expect(status).ToContainTextAsync("Für heute steht nichts an.");
        await Assertions.Expect(page.Page.Locator(".plan-block")).ToHaveCountAsync(0);
    }

    private static async Task AssertNoSidewaysScrollAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways.");
    }
}