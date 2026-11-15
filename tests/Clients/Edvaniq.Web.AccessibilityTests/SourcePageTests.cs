using System.Text.Json;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #351 on a phone, only with the keyboard: a question says it is AI text, after the answer it names its source, and the
// place in the original opens: the passage of a pasted text marked, a PDF as a link at its page. Each state passes axe.
public sealed class SourcePageTests(WebShell shell)
{
    private static readonly Guid SectionId = Guid.Parse("4d5e6f7a-8b9c-4d0e-1f2a-3b4c5d6e7f80");

    private static string Path => $"/themen/{Material.TopicId}/ueben";

    [Fact]
    public async Task PastedText_OpensAtThePassage_WithTheKeyboardOnly()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser => AnswerAsync(browser, new
        {
            documentId = Material.DocumentId,
            sectionId = SectionId,
            title = "Skript Biologie",
            format = (string?)null,
            hasFile = false,
            text = "Davor steht etwas. Die Lichtreaktion läuft an den Thylakoiden ab. Danach geht es weiter.",
            start = 19,
            length = 46,
            truncatedBefore = true,
            truncatedAfter = false,
            position = 1,
        }));

        await Assertions.Expect(page.Page.Locator(".quiz-form .ai-note")).ToContainTextAsync("KI-generiert");
        await Assertions.Expect(page.Page.Locator(".quiz-form .ai-note")).ToContainTextAsync("KI kann Fehler machen.");
        await Assertions.Expect(page.Page.Locator(".source")).ToHaveCountAsync(0);
        await AnswerWithTheKeyboardAsync(page.Page);

        var toggle = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Stelle ansehen in „Skript Biologie“" });
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} answered, with its source at {WebShell.PhoneWidth} px");

        await TabToAsync(page.Page, toggle);
        await page.Page.Keyboard.PressAsync("Enter");

        var open = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Stelle ausblenden in „Skript Biologie“" });
        await Assertions.Expect(open).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(open).ToBeFocusedAsync();
        await Assertions.Expect(page.Page.Locator(".source-place")).ToHaveTextAsync("Abschnitt 2");
        await Assertions.Expect(page.Page.Locator(".source-window mark")).ToHaveTextAsync("Die Lichtreaktion läuft an den Thylakoiden ab.");
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with the passage open at {WebShell.PhoneWidth} px");
    }

    [Fact]
    public async Task Pdf_OpensAtItsPage_InANewTab()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser => AnswerAsync(browser, new
        {
            documentId = Material.DocumentId,
            sectionId = SectionId,
            title = "Skript Biologie",
            format = "pdf",
            hasFile = true,
            page = 3,
            pageTo = 4,
            text = (string?)null,
            start = (int?)null,
            length = (int?)null,
            truncatedBefore = false,
            truncatedAfter = false,
            position = 2,
        }));
        await AnswerWithTheKeyboardAsync(page.Page);

        var toggle = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Stelle ansehen in „Skript Biologie“" });
        await TabToAsync(page.Page, toggle);
        await page.Page.Keyboard.PressAsync("Space");

        var link = page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Original öffnen „Skript Biologie“, S. 3 (öffnet in neuem Tab)" });
        await Assertions.Expect(link).ToHaveAttributeAsync("href", $"api/content/documents/{Material.DocumentId}/file#page=3");
        await Assertions.Expect(link).ToHaveAttributeAsync("target", "_blank");
        await Assertions.Expect(link).ToHaveAttributeAsync("rel", "noopener");
        await page.Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(link).ToBeFocusedAsync();
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with the PDF link at {WebShell.PhoneWidth} px");
    }

    // The question has a source, answering it is right, the documents and the section come from Content.
    private static async Task AnswerAsync(IPage browser, object section)
    {
        await Material.AnswerAsync(browser, documents: [new[] { Material.Document("Ready") }], topics: [Material.Topics]);
        await browser.RouteAsync("**/api/assessment/attempts", route => FulfillAsync(route, new
        {
            id = Material.AttemptId,
            topicId = Material.TopicId,
            questionId = Guid.NewGuid(),
            type = "singleChoice",
            text = "Wo läuft die Lichtreaktion ab?",
            options = new[] { "Im Zellkern", "In den Thylakoiden" },
            difficulty = 1,
            source = new { documentId = Material.DocumentId, sectionId = SectionId, model = "m", promptVersion = "questions-3" },
        }, 201));
        await browser.RouteAsync("**/api/assessment/attempts/*/answer", route => FulfillAsync(route, new
        {
            id = Material.AttemptId,
            topicId = Material.TopicId,
            questionId = Guid.NewGuid(),
            type = "singleChoice",
            option = 1,
            correct = true,
            answer = 1,
            answers = new[] { 1 },
            explanation = "Die Lichtreaktion läuft an den Membranen der Thylakoide ab.",
            responseTimeMs = 1000,
            answeredAt = DateTime.UtcNow,
        }));
        await browser.RouteAsync($"**/api/content/sections/{SectionId}/original", route => FulfillAsync(route, section));
    }

    private static async Task AnswerWithTheKeyboardAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("legend")).ToHaveTextAsync("Wo läuft die Lichtreaktion ab?");
        await TabToAsync(page, page.Locator("#option-0"));
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Locator("#option-1")).ToBeCheckedAsync();
        await TabToAsync(page, page.Locator("button[type=submit]"));
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#feedback-heading")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator(".quiz-feedback .source-title")).ToHaveTextAsync("Skript Biologie");
    }

    private static Task FulfillAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        });

    // Presses Tab until the element has the focus, at most 40 times.
    private static async Task TabToAsync(IPage page, ILocator target)
    {
        for (var i = 0; i < 40; i++)
        {
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.Fail("Tab never reached the element.");
    }

    private static async Task AssertNoSidewaysScrollAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways.");
    }
}