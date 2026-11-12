using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #379: the page /plan reads on a phone, a tablet and a desktop: nothing scrolls sideways, also with a topic name that
// is one long word, axe-core finds nothing, and the keyboard gets from the menu to the page. Empty, it leads to a new
// learning goal.
public sealed class WeekPlanPageTests(WebShell shell)
{
    private const string Path = "/plan";

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.TabletWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task Week_ReadsAtEveryWidth(int width)
    {
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: browser => Material.AnswerWeekAsync(browser, Material.Week()));
        var days = page.Page.Locator(".week-day");
        await Assertions.Expect(days).ToHaveCountAsync(3);
        await Assertions.Expect(days.Nth(0).Locator("h2")).ToHaveTextAsync("Heute, Mittwoch, 11. November");
        await Assertions.Expect(days.Nth(0).Locator(".week-block")).ToHaveCountAsync(2);
        await Assertions.Expect(days.Nth(0).Locator(".week-block").Nth(1)).ToContainTextAsync(Material.LongWord);
        await Assertions.Expect(days.Nth(0).Locator(".week-block").Nth(1)).ToContainTextAsync("gekürzt");
        await Assertions.Expect(days.Nth(1)).ToContainTextAsync("Nichts geplant.");
        await Assertions.Expect(page.Page.Locator(".plan-shortened")).ToContainTextAsync(Material.LongWord);

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with a plan at {width} px");
    }

    [Fact]
    public async Task EmptyWeek_LeadsToANewLearningGoal()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerWeekAsync(browser, Material.Week("noTopics")));
        var next = page.Page.Locator(".state-actions").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Lernziel anlegen", Exact = true });
        await Assertions.Expect(next).ToBeVisibleAsync();

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} without topics at {WebShell.PhoneWidth} px");

        await next.FocusAsync();
        await page.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/lernziele/neu$"));
    }

    private static async Task AssertNoSidewaysScrollAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways.");
    }
}