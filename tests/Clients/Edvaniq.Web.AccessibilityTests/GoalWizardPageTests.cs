using System.Text.Json;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #273 AC4: the assistant works on a phone with the keyboard alone, from the kind to the saved goal. Nothing scrolls
// sideways, and axe-core finds nothing on any step.
public sealed class GoalWizardPageTests(WebShell shell)
{
    private const string Path = "/lernziele/neu";

    [Fact]
    public async Task KeyboardAlone_LeadsThroughEveryStep_OnAPhone()
    {
        var created = new List<string>();
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerGoalsAsync(browser, created));
        var keys = page.Page.Keyboard;
        var heading = page.Page.Locator("#goal-step-heading");
        await Assertions.Expect(heading).ToContainTextAsync("Schritt 1 von 4");
        await CheckAsync(page.Page, "step 1");

        // Into the group of kinds, the first one is chosen with the space bar, then on to "Weiter".
        await page.Page.Locator("#goal-kind-ExamPreparation").FocusAsync();
        await keys.PressAsync("Space");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Schritt 2 von 4");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await CheckAsync(page.Page, "step 2");
        await keys.PressAsync("Tab");
        await keys.TypeAsync("Biologie-Klausur");
        await keys.PressAsync("Tab");
        await keys.TypeAsync("Klausur");
        await keys.PressAsync("Shift+Tab");
        await keys.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Schritt 3 von 4");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await CheckAsync(page.Page, "step 3");
        await keys.PressAsync("Tab");
        await keys.TypeAsync("Oberstufe");
        await keys.PressAsync("Tab");
        await keys.PressAsync("ArrowDown");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Schritt 4 von 4");
        await Assertions.Expect(page.Page.Locator(".goal-summary")).ToContainTextAsync("Biologie-Klausur");
        await CheckAsync(page.Page, "step 4");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("[role=status] .goal-saved")).ToBeVisibleAsync();
        await CheckAsync(page.Page, "saved");
        using var sent = JsonDocument.Parse(Assert.Single(created));
        var goal = sent.RootElement;
        Assert.Equal("ExamPreparation", goal.GetProperty("kind").GetString());
        Assert.Equal("Biologie-Klausur", goal.GetProperty("name").GetString());
        Assert.Equal("Klausur", goal.GetProperty("examType").GetString());
        Assert.Equal("UpperSecondary", goal.GetProperty("level").GetString());
        Assert.Equal("en", goal.GetProperty("outputLanguage").GetString());
    }

    // An error at its field is as readable as the rest, and the focus goes to the field.
    [Fact]
    public async Task MissingKind_IsSaidAtTheField()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerGoalsAsync(browser, []));
        await Assertions.Expect(page.Page.Locator("#goal-step-heading")).ToContainTextAsync("Schritt 1 von 4");

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Weiter" }).PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("#goal-kind-error")).ToContainTextAsync("Wähle, ob du auf eine Prüfung lernst");
        await Assertions.Expect(page.Page.Locator("#goal-kind-ExamPreparation")).ToBeFocusedAsync();
        await CheckAsync(page.Page, "step 1 with an error");
    }

    private static async Task CheckAsync(IPage page, string state)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways at {state}.");
        await AxeScan.AssertNoViolationsAsync(page, $"{Path} at {state}");
    }
}