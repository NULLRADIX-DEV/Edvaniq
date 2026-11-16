using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #357: a block's quiz on a phone with the keyboard only, one question of each type, then the tally. The focus follows
// to each feedback and each next question, nothing scrolls sideways, and each state passes axe-core.
public sealed class BlockQuizPageTests(WebShell shell)
{
    private static string Path => $"/themen/{Material.TopicId}/quiz/{Material.BlockId}";

    [Fact]
    public async Task ThreeTypes_OnAPhone_WithTheKeyboardOnly()
    {
        var sent = new List<string>();
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser => Material.AnswerBlockQuizAsync(browser, sent));
        var legend = page.Page.Locator("legend");
        var progress = page.Page.Locator(".block-quiz-progress");
        var heading = page.Page.Locator("#feedback-heading");
        var next = page.Page.Locator(".quiz-feedback ~ .quiz-actions button");

        await Assertions.Expect(legend).ToHaveTextAsync("Wo findet die Lichtreaktion statt?");
        await Assertions.Expect(progress).ToHaveTextAsync("Frage 1 von 3");
        await AssertCleanAsync(page.Page, "single choice");
        await TabToAsync(page.Page, "#option-0");
        await page.Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Page.GetByLabel("In den Thylakoiden")).ToBeCheckedAsync();
        await TabToAsync(page.Page, "button[type=submit]");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Richtig! Gut gemacht.");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await AssertCleanAsync(page.Page, "single choice answered");
        await TabToAsync(page.Page, ".quiz-feedback ~ .quiz-actions button");
        await Assertions.Expect(next).ToHaveTextAsync("Nächste Frage");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(legend).ToHaveTextAsync("Was braucht die Photosynthese?");
        await Assertions.Expect(legend).ToBeFocusedAsync();
        await Assertions.Expect(progress).ToHaveTextAsync("Frage 2 von 3");
        await AssertCleanAsync(page.Page, "multiple choice");
        await TabToAsync(page.Page, "#option-0");
        await page.Page.Keyboard.PressAsync("Space");
        await TabToAsync(page.Page, "#option-2");
        await page.Page.Keyboard.PressAsync("Space");
        await TabToAsync(page.Page, "button[type=submit]");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Richtig! Gut gemacht.");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await TabToAsync(page.Page, ".quiz-feedback ~ .quiz-actions button");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(legend).ToHaveTextAsync("Die Lichtreaktion braucht Licht.");
        await Assertions.Expect(legend).ToBeFocusedAsync();
        await Assertions.Expect(progress).ToHaveTextAsync("Frage 3 von 3");
        await AssertCleanAsync(page.Page, "true or false");
        await TabToAsync(page.Page, "#option-0");
        await page.Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Page.GetByLabel("Falsch")).ToBeCheckedAsync();
        await TabToAsync(page.Page, "button[type=submit]");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Diesmal nicht richtig.");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await AssertCleanAsync(page.Page, "true or false answered wrong");
        await TabToAsync(page.Page, ".quiz-feedback ~ .quiz-actions button");
        await Assertions.Expect(next).ToHaveTextAsync("Zum Ergebnis");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("#block-quiz-done-heading")).ToBeFocusedAsync();
        await Assertions.Expect(page.Page.Locator(".block-quiz-tally")).ToHaveTextAsync("2 von 3 Fragen richtig.");
        await AssertCleanAsync(page.Page, "done");
        Assert.Equal(3, sent.Count);
        await AxeScan.AssertHeadingOrderAsync(page.Page, $"{Path} done at {WebShell.PhoneWidth} px");
    }

    private static async Task AssertCleanAsync(IPage page, string state)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways ({state}).");
        await AxeScan.AssertNoViolationsAsync(page, $"{Path} {state} at {WebShell.PhoneWidth} px");
    }

    // Presses Tab until the element has the focus, at most 30 times.
    private static async Task TabToAsync(IPage page, string selector)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await page.Locator(selector).EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.Fail($"Tab never reached {selector}.");
    }
}