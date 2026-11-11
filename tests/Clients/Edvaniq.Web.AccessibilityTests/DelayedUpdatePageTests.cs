using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #222 in the browser: after an answer the practice page and Home say the new state is on its way, then show it by
// themselves. Each state passes axe-core.
public sealed class DelayedUpdatePageTests(WebShell shell)
{
    private static string Path => $"/themen/{Material.TopicId}/ueben";

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task Answer_ShowsTheHint_ThenTheNewMastery(int width)
    {
        var arrived = false;
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: async browser =>
        {
            await Material.AnswerQuizAsync(browser);
            await Material.AnswerMasteryAsync(browser, () => arrived);
        });
        await page.Page.GetByLabel("In den Thylakoiden").CheckAsync();

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" }).ClickAsync();

        var progress = page.Page.Locator(".answer-progress");
        await Assertions.Expect(progress).ToContainTextAsync("Dein Lernstand wird aktualisiert");
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} while the mastery is on its way at {width} px");

        arrived = true;

        await Assertions.Expect(progress).ToContainTextAsync("62 %, im Aufbau");
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with the new mastery at {width} px");
    }

    // From the answer straight to Home: the plan says it is being updated, then loads anew without a click.
    [Fact]
    public async Task Home_AfterAnAnswer_ShowsTheHint_ThenTheNewPlan()
    {
        var arrived = false;
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.DesktopWidth, prepare: async browser =>
        {
            await Material.AnswerQuizAsync(browser);
            await Material.AnswerMasteryAsync(browser, () => arrived);
            await Material.AnswerPlanAsync(browser, Material.Plan(), Material.Plan("allMastered"));
        });
        await page.Page.GetByLabel("In den Thylakoiden").CheckAsync();
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" }).ClickAsync();
        await Assertions.Expect(page.Page.Locator(".answer-progress")).ToBeVisibleAsync();

        await page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Start", Exact = true }).ClickAsync();

        var status = page.Page.Locator(".plan-status");
        await Assertions.Expect(status).ToContainTextAsync("Dein Plan wird gerade aktualisiert");
        await Assertions.Expect(page.Page.Locator(".plan-blocks")).ToHaveAttributeAsync("aria-busy", "true");
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/ while the plan is on its way at {WebShell.DesktopWidth} px");

        arrived = true;

        await Assertions.Expect(status).ToContainTextAsync("Für heute steht nichts an.");
        await Assertions.Expect(page.Page.Locator(".plan-block")).ToHaveCountAsync(0);
    }

    private static async Task AssertNoSidewaysScrollAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways.");
    }
}