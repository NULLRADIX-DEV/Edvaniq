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

    // #382: minutes, blocks and the countdown above the list, on a phone and a desktop.
    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task Summary_AndCountdown_ReadAtBothWidths(int width)
    {
        await using var page = await shell.OpenSignedInAsync("/", width, prepare: browser =>
            Material.AnswerPlanAsync(browser, Material.Plan(daysToExam: 110)));

        await Assertions.Expect(page.Page.Locator(".today-total")).ToHaveTextAsync("Heute: 45 Minuten in 3 Blöcken");
        await Assertions.Expect(page.Page.Locator(".today-exam")).ToHaveTextAsync("Noch 110 Tage bis zur Prüfung am 1. März 2027");

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/ with summary and countdown at {width} px");
    }

    // #382: without an exam no countdown.
    [Fact]
    public async Task WithoutAnExam_ThereIsNoCountdown()
    {
        await using var page = await shell.OpenSignedInAsync("/", WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerPlanAsync(browser, Material.Plan()));

        await Assertions.Expect(page.Page.Locator(".today-total")).ToHaveTextAsync("Heute: 45 Minuten in 3 Blöcken");
        await Assertions.Expect(page.Page.Locator(".today-exam")).ToHaveCountAsync(0);
    }

    // #382 AC4: Planning out of reach shows the problem and a retry, which then brings the plan.
    [Fact]
    public async Task NoConnection_ShowsTheProblem_AndRetryBringsThePlan()
    {
        var calls = 0;
        await using var page = await shell.OpenSignedInAsync("/", WebShell.PhoneWidth, prepare: async browser =>
        {
            await Material.AnswerPlanAsync(browser, Material.Plan(daysToExam: 3));
            await browser.RouteAsync("**/api/planning/plan/today", route =>
                Interlocked.Increment(ref calls) == 1 ? route.AbortAsync("internetdisconnected") : route.FallbackAsync());
        });
        var status = page.Page.Locator(".plan-status");
        await Assertions.Expect(status).ToContainTextAsync("Dein Plan für heute ließ sich gerade nicht laden.");
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/ without connection at {WebShell.PhoneWidth} px");

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Erneut versuchen" }).ClickAsync();

        await Assertions.Expect(page.Page.Locator(".today-exam")).ToHaveTextAsync("Noch 3 Tage bis zur Prüfung am 1. März 2027");
    }

    private static async Task AssertNoSidewaysScrollAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways.");
    }
}