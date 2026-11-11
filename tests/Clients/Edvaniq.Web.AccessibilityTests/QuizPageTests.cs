using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// A question on a phone: from the topic to the question, an answer with the thumb and the feedback, nothing scrolls
// sideways. Each state passes axe-core.
public sealed class QuizPageTests(WebShell shell)
{
    // WCAG 2.5.5 asks for 44 CSS pixels.
    private const float SmallestTarget = 44;

    private static string Path => $"/themen/{Material.TopicId}/ueben";

    [Fact]
    public async Task FromTheTopic_ToTheFeedback_OnAPhone()
    {
        await using var page = await shell.OpenSignedInAsync("/themen", WebShell.PhoneWidth, prepare: browser => Material.AnswerQuizAsync(browser));
        var practice = page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Üben zu „Photosynthese“" });
        await AssertLargeEnoughAsync(practice);

        await practice.ClickAsync();

        await Assertions.Expect(page.Page.Locator("legend")).ToContainTextAsync("Wo findet die Lichtreaktion statt?");
        await Assertions.Expect(page.Page.Locator("h1")).ToHaveTextAsync("Frage zu „Photosynthese“");
        var right = page.Page.GetByLabel("In den Thylakoiden");
        var check = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" });
        await AssertLargeEnoughAsync(page.Page.Locator("label[for=option-1]"));
        await AssertLargeEnoughAsync(check);
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with a question at {WebShell.PhoneWidth} px");

        await right.CheckAsync();
        await check.ClickAsync();

        await Assertions.Expect(page.Page.Locator("#feedback-heading")).ToContainTextAsync("Richtig! Gut gemacht.");
        await Assertions.Expect(page.Page.Locator("#feedback-heading")).ToBeFocusedAsync();
        await Assertions.Expect(check).ToHaveCountAsync(0);
        var next = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Nächste Frage" });
        await AssertLargeEnoughAsync(next);
        await AssertLargeEnoughAsync(page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Zurück zu den Themen" }));
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} answered right at {WebShell.PhoneWidth} px");

        await next.ClickAsync();

        await Assertions.Expect(page.Page.Locator("legend")).ToBeFocusedAsync();
        await Assertions.Expect(page.Page.Locator(".quiz-feedback")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task WrongAnswer_HasNoViolations(int width)
    {
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: browser => Material.AnswerQuizAsync(browser));
        await page.Page.GetByLabel("Im Zellkern").CheckAsync();

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" }).ClickAsync();

        await Assertions.Expect(page.Page.Locator("#feedback-heading")).ToContainTextAsync("Diesmal nicht richtig.");
        await Assertions.Expect(page.Page.Locator(".quiz-feedback")).ToContainTextAsync("Richtige Antwort: In den Thylakoiden");
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} answered wrong at {width} px");
    }

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task RightAnswer_HasNoViolations(int width)
    {
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: browser => Material.AnswerQuizAsync(browser));
        await page.Page.GetByLabel("In den Thylakoiden").CheckAsync();

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" }).ClickAsync();

        await Assertions.Expect(page.Page.Locator("#feedback-heading")).ToContainTextAsync("Richtig!");
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} answered right at {width} px");
    }

    // AC3: no questions yet is a hint with a way on, at a size for the thumb.
    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task NoQuestionsYet_ShowsAHint_WithoutViolations(int width)
    {
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: browser => Material.AnswerQuizAsync(browser, ready: false));

        await Assertions.Expect(page.Page.Locator(".quiz-status")).ToContainTextAsync("Zu diesem Thema gibt es noch keine Fragen.");
        await AssertLargeEnoughAsync(page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Noch einmal versuchen" }));
        await AssertLargeEnoughAsync(page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Anderes Thema wählen" }));
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} without questions at {width} px");
    }

    private static async Task AssertLargeEnoughAsync(ILocator target)
    {
        var box = await target.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Height >= SmallestTarget, $"The target is {box.Height} px high.");
        Assert.True(box.Width >= SmallestTarget, $"The target is {box.Width} px wide.");
    }

    private static async Task AssertNoSidewaysScrollAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways.");
    }
}