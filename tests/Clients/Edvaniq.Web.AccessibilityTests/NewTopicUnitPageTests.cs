using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #358: a new topic's unit on a phone with the keyboard only, once with check questions and once without. The focus
// follows each step, nothing scrolls sideways, and each state passes axe-core.
public sealed class NewTopicUnitPageTests(WebShell shell)
{
    private static string Path => $"/themen/{Material.TopicId}/einheit";

    [Fact]
    public async Task WithPool_SummaryThenChecksThenResult_WithTheKeyboardOnly()
    {
        List<string> started = [], sent = [];
        await using var page = await shell.OpenSignedInAsync(
            Path, WebShell.PhoneWidth, prepare: browser => Material.AnswerNewTopicUnitAsync(browser, pool: true, started, sent));
        var legend = page.Page.Locator("legend");
        var heading = page.Page.Locator("#feedback-heading");
        var next = page.Page.Locator(".quiz-feedback ~ .quiz-actions button");

        await AssertSummaryStepAsync(page.Page);
        Assert.Empty(started);
        await TabToAsync(page.Page, ".unit-actions button");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(legend).ToHaveTextAsync("Wo findet die Lichtreaktion statt?");
        await Assertions.Expect(legend).ToBeFocusedAsync();
        await Assertions.Expect(page.Page.Locator(".unit-step")).ToHaveTextAsync("Schritt 2 von 3: Checkfragen");
        await Assertions.Expect(page.Page.Locator(".block-quiz-progress")).ToHaveTextAsync("Frage 1 von 2");
        await AssertCleanAsync(page.Page, "first check question");
        await TabToAsync(page.Page, "#option-0");
        await page.Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Page.GetByLabel("In den Thylakoiden")).ToBeCheckedAsync();
        await TabToAsync(page.Page, "button[type=submit]");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Richtig! Gut gemacht.");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await AssertCleanAsync(page.Page, "first check question answered");
        await TabToAsync(page.Page, ".quiz-feedback ~ .quiz-actions button");
        await Assertions.Expect(next).ToHaveTextAsync("Nächste Frage");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(legend).ToHaveTextAsync("Die Lichtreaktion braucht Licht.");
        await Assertions.Expect(legend).ToBeFocusedAsync();
        await TabToAsync(page.Page, "#option-0");
        await page.Page.Keyboard.PressAsync("Space");
        await Assertions.Expect(page.Page.GetByLabel("Wahr")).ToBeCheckedAsync();
        await TabToAsync(page.Page, "button[type=submit]");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(heading).ToBeFocusedAsync();
        await TabToAsync(page.Page, ".quiz-feedback ~ .quiz-actions button");
        await Assertions.Expect(next).ToHaveTextAsync("Zum Ergebnis");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("#unit-done-heading")).ToBeFocusedAsync();
        await Assertions.Expect(page.Page.Locator(".unit-summary-line")).ToHaveTextAsync("Zusammenfassung gelesen.");
        await Assertions.Expect(page.Page.Locator(".unit-tally")).ToHaveTextAsync("2 von 2 Checkfragen richtig.");
        await AssertCleanAsync(page.Page, "done");
        await AxeScan.AssertHeadingOrderAsync(page.Page, $"{Path} done at {WebShell.PhoneWidth} px");
        Assert.Single(started);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public async Task WithoutPool_TheSummaryAlone_WithTheKeyboardOnly()
    {
        List<string> started = [], sent = [];
        await using var page = await shell.OpenSignedInAsync(
            Path, WebShell.PhoneWidth, prepare: browser => Material.AnswerNewTopicUnitAsync(browser, pool: false, started, sent));
        var hint = page.Page.Locator("#unit-no-checks-heading");

        await AssertSummaryStepAsync(page.Page);
        await TabToAsync(page.Page, ".unit-actions button");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(hint).ToHaveTextAsync("Für dieses Thema gibt es noch keine Checkfragen.");
        await Assertions.Expect(hint).ToBeFocusedAsync();
        await AssertCleanAsync(page.Page, "without check questions");
        await TabToAsync(page.Page, ".unit-no-checks button");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("#unit-done-heading")).ToBeFocusedAsync();
        await Assertions.Expect(page.Page.Locator(".unit-tally")).ToHaveTextAsync("Checkfragen gibt es für dieses Thema noch nicht.");
        await AssertCleanAsync(page.Page, "done without check questions");
        await AxeScan.AssertHeadingOrderAsync(page.Page, $"{Path} done without check questions at {WebShell.PhoneWidth} px");
        Assert.Single(started);
        Assert.Empty(sent);
    }

    private static async Task AssertSummaryStepAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Neues Thema");
        await Assertions.Expect(page.Locator(".unit-step")).ToHaveTextAsync("Schritt 1 von 3: Zusammenfassung");
        await Assertions.Expect(page.Locator("h2").First).ToHaveTextAsync("Zusammenfassung zu „Photosynthese“");
        await Assertions.Expect(page.Locator(".summary-text p").First).ToHaveTextAsync("Die Photosynthese wandelt Licht in chemische Energie um.");
        await AssertCleanAsync(page, "summary");
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