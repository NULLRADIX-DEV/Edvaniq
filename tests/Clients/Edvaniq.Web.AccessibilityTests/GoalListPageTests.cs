using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #281 AC3: the overview of the goals reads on a phone, with goals and without: nothing scrolls sideways, also with a
// name that is one long word, every way on is big enough to hit, and axe-core finds nothing.
public sealed class GoalListPageTests(WebShell shell)
{
    private const string Path = "/lernziele";

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task Goals_ReadAtEveryWidth(int width)
    {
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: browser =>
            Material.AnswerGoalListAsync(browser, Material.Goals));
        var cards = page.Page.Locator(".goal-card");
        await Assertions.Expect(cards).ToHaveCountAsync(2);
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("Klausur am 1. März 2027");
        await Assertions.Expect(cards.Nth(1)).ToContainTextAsync("Freies Lernen");

        // Material and, since #294, the topic directory.
        var links = cards.Nth(0).GetByRole(AriaRole.Link);
        await Assertions.Expect(links).ToHaveCountAsync(2);
        foreach (var link in await links.AllAsync())
        {
            await AssertLargeEnoughAsync(link);
        }

        await AssertLargeEnoughAsync(page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Neues Lernziel" }));
        await CheckAsync(page.Page, $"{Path} with goals at {width} px");
    }

    [Fact]
    public async Task WithoutGoals_LeadsToTheAssistant()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerGoalListAsync(browser, []));
        var create = page.Page.Locator(".state-actions").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Lernziel anlegen" });
        await Assertions.Expect(create).ToBeVisibleAsync();
        await AssertLargeEnoughAsync(create);
        await CheckAsync(page.Page, $"{Path} without goals");

        await create.ClickAsync();

        await Assertions.Expect(page.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/lernziele/neu$"));
    }

    private static async Task AssertLargeEnoughAsync(ILocator target)
    {
        var box = (await target.BoundingBoxAsync())!;
        Assert.True(box.Height >= 44, $"The target is only {box.Height} px high.");
    }

    private static async Task CheckAsync(IPage page, string state)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways at {state}.");
        await AxeScan.AssertNoViolationsAsync(page, state);
    }
}