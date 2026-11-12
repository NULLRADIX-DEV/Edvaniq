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
        var added = new List<string>();
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: async browser =>
        {
            await Material.AnswerGoalsAsync(browser, created);
            await Material.AnswerEnteredTopicsAsync(browser, added);
        });
        var keys = page.Page.Keyboard;
        var heading = page.Page.Locator("#goal-step-heading");
        await Assertions.Expect(heading).ToContainTextAsync("Schritt 1 von 5");
        await CheckAsync(page.Page, "step 1");

        // Into the group of kinds, the first one is chosen with the space bar, then on to "Weiter".
        await page.Page.Locator("#goal-kind-ExamPreparation").FocusAsync();
        await keys.PressAsync("Space");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Schritt 2 von 5");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await CheckAsync(page.Page, "step 2");
        await keys.PressAsync("Tab");
        await keys.TypeAsync("Biologie-Klausur");
        await keys.PressAsync("Tab");
        await keys.TypeAsync("Klausur");
        await keys.PressAsync("Shift+Tab");
        await keys.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Schritt 3 von 5");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await CheckAsync(page.Page, "step 3");
        await keys.PressAsync("Tab");
        await keys.TypeAsync("Oberstufe");
        await keys.PressAsync("Tab");
        await keys.PressAsync("ArrowDown");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Enter");

        // #317: two topics, the second one the same name as the first, the field for it from the button.
        await Assertions.Expect(heading).ToContainTextAsync("Schritt 4 von 5");
        await Assertions.Expect(heading).ToBeFocusedAsync();
        await CheckAsync(page.Page, "step 4");
        await keys.PressAsync("Tab");
        await keys.TypeAsync("Zellatmung");
        await keys.PressAsync("Tab");
        await keys.PressAsync("Enter");
        await Assertions.Expect(page.Page.Locator("#goal-topic-2")).ToBeFocusedAsync();
        await keys.TypeAsync("  zellatmung");
        await CheckAsync(page.Page, "step 4 with two topics");
        await keys.PressAsync("Enter");

        await Assertions.Expect(heading).ToContainTextAsync("Schritt 5 von 5");
        await Assertions.Expect(page.Page.Locator(".goal-summary")).ToContainTextAsync("Biologie-Klausur");
        await Assertions.Expect(page.Page.Locator(".goal-topics li")).ToHaveTextAsync(["Zellatmung"]);
        await CheckAsync(page.Page, "step 5");
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

        // #317 AC1: the topic is in the list of topics, once.
        await Assertions.Expect(page.Page.Locator("[role=status] .goal-saved")).ToContainTextAsync("Deine Themen stehen jetzt in deiner Themenübersicht.");
        using var topic = JsonDocument.Parse(Assert.Single(added));
        Assert.Equal("Zellatmung", topic.RootElement.GetProperty("name").GetString());
        Assert.NotEqual(Guid.Empty, topic.RootElement.GetProperty("learningGoalId").GetGuid());
        await page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Text einfügen" }).FocusAsync();
        await keys.PressAsync("Enter");
        await Assertions.Expect(page.Page).ToHaveURLAsync(shell.Address + "/themen");
        await Assertions.Expect(page.Page.GetByText("Zellatmung", new PageGetByTextOptions { Exact = true })).ToHaveCountAsync(1);
    }

    // An error at its field is as readable as the rest, and the focus goes to the field.
    [Fact]
    public async Task MissingKind_IsSaidAtTheField()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerGoalsAsync(browser, []));
        await Assertions.Expect(page.Page.Locator("#goal-step-heading")).ToContainTextAsync("Schritt 1 von 5");

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