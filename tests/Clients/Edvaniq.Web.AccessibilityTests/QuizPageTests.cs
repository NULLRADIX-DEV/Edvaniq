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
        await AssertLargeEnoughAsync(page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Lösung anzeigen" }));
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

    // #196 AC3 and AC4: the connection breaks while the answer is on its way. The page says so without anything
    // technical, and sending it again goes to the same attempt, which the server grades only once.
    [Fact]
    public async Task AnswerLostOnTheWay_IsSentAgain_ToTheSameAttempt()
    {
        var answers = new List<string>();
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: async browser =>
        {
            await Material.AnswerQuizAsync(browser);
            await browser.RouteAsync("**/api/assessment/attempts/*/answer", route =>
            {
                answers.Add(route.Request.Url);
                return answers.Count == 1 ? route.AbortAsync("internetdisconnected") : route.FallbackAsync();
            });
        });
        await page.Page.GetByLabel("In den Thylakoiden").CheckAsync();
        var check = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" });

        await check.ClickAsync();

        var alert = page.Page.Locator("form [role=alert]");
        await Assertions.Expect(alert).ToContainTextAsync("Deine Antwort ist nicht angekommen.");
        await Assertions.Expect(alert).ToContainTextAsync("Prüf deine Internetverbindung.");
        Assert.DoesNotMatch("""\d{3}|Exception|http|/api/""", await alert.InnerTextAsync());
        await Assertions.Expect(page.Page.GetByLabel("In den Thylakoiden")).ToBeCheckedAsync();
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with a lost answer at {WebShell.PhoneWidth} px");

        await check.ClickAsync();

        await Assertions.Expect(page.Page.Locator("#feedback-heading")).ToContainTextAsync("Richtig! Gut gemacht.");
        Assert.Equal(2, answers.Count);
        Assert.Equal(answers[0], answers[1]);
    }

    // AC2 of #347: an explanation in Markdown with headings, a nested list, a table, emphasis and a link. Its headings
    // sit below the feedback's, a wide table scrolls on its own, and the page passes axe.
    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task MarkdownExplanation_HasNoViolations(int width)
    {
        const string explanation = """
            # Lichtreaktion

            Sie läuft an den **Membranen** der *Thylakoide* ab, nicht im ~~Zellkern~~. Mehr bei [Wikipedia](https://de.wikipedia.org/wiki/Lichtreaktion).

            ## Ablauf

            1. Licht trifft auf `Chlorophyll`
               - Elektronen werden angeregt
               - Wasser wird gespalten
            2. ATP und NADPH entstehen

            | Ort | Stoff | Menge je Durchgang |
            |:---|:---:|---:|
            | Thylakoidmembran | Sauerstoff (O₂) aus der Fotolyse des Wassers | 1 Molekül je 2 Moleküle Wasser |
            | Stroma | NADPH | 2 Moleküle |
            """;
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: browser => Material.AnswerQuizAsync(browser, explanation: explanation));
        await page.Page.GetByLabel("Im Zellkern").CheckAsync();

        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" }).ClickAsync();

        var shown = page.Page.Locator(".quiz-explanation .markdown");
        await Assertions.Expect(shown.Locator("h3")).ToHaveTextAsync("Lichtreaktion");
        await Assertions.Expect(shown.Locator("h4")).ToHaveTextAsync("Ablauf");
        await Assertions.Expect(shown.Locator("strong")).ToHaveTextAsync("Membranen");
        await Assertions.Expect(shown.Locator("ol > li > ul > li")).ToHaveCountAsync(2);
        await Assertions.Expect(shown.Locator("thead th[scope=col]")).ToHaveCountAsync(3);
        await Assertions.Expect(shown.Locator("tbody tr")).ToHaveCountAsync(2);
        await Assertions.Expect(shown.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Wikipedia (öffnet in neuem Tab)" }))
            .ToHaveAttributeAsync("rel", "noopener noreferrer nofollow");
        Assert.Equal(["H1", "H2", "H3", "H4"], await page.Page.Locator("main :is(h1, h2, h3, h4, h5, h6)").EvaluateAllAsync<string[]>("all => all.map(h => h.tagName)"));
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with a Markdown explanation at {width} px");
        await AxeScan.AssertHeadingOrderAsync(page.Page, $"{Path} with a Markdown explanation at {width} px");
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