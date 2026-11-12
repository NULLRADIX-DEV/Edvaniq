using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// The topics page on a phone: pasting a text and sending it works with the thumb, nothing scrolls sideways, and the
// topics come in by themselves. Each state passes axe-core.
public sealed class TopicsPageTests(WebShell shell)
{
    // WCAG 2.5.5 asks for 44 CSS pixels.
    private const float SmallestTarget = 44;

    [Fact]
    public async Task PasteAndSend_OnAPhone_BringsTheTopics()
    {
        await using var page = await shell.OpenSignedInAsync("/themen", WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerAsync(browser, documents: [Array.Empty<object>(), new[] { Material.Document("Ready") }], topics: [Array.Empty<object>(), Material.Topics]));
        var text = page.Page.GetByLabel("Text", new PageGetByLabelOptions { Exact = true });
        var send = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Themen erkennen" });
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync("Du hast noch keinen Text eingefügt.");

        await AssertLargeEnoughAsync(text);
        await AssertLargeEnoughAsync(send);
        await AssertLargeEnoughAsync(page.Page.GetByLabel("Titel"));

        await text.FillAsync("Die Photosynthese wandelt Lichtenergie in chemische Energie um.");
        await send.ClickAsync();

        await Assertions.Expect(page.Page.Locator(".topic-list")).ToContainTextAsync("Photosynthese", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync("sind da");
        await Assertions.Expect(text).ToHaveValueAsync("");
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/themen with topics at {WebShell.PhoneWidth} px");
    }

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task WithoutADocument_HasNoViolations(int width)
    {
        await using var page = await shell.OpenSignedInAsync("/themen", width, prepare: browser =>
            Material.AnswerAsync(browser, documents: [Array.Empty<object>()], topics: [Array.Empty<object>()]));
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync("Du hast noch keinen Text eingefügt.");

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/themen without a document at {width} px");
    }

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task WhileAnalyzing_HasNoViolations(int width)
    {
        await using var page = await shell.OpenSignedInAsync("/themen", width, prepare: browser =>
            Material.AnswerAsync(browser, documents: [new[] { Material.Document("Analyzing") }], topics: [Array.Empty<object>()]));
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync("wird analysiert");

        await AxeScan.AssertNoViolationsAsync(page.Page, $"/themen while analyzing at {width} px");
    }

    // The hint after a failed analysis is in the red pen colors.
    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task FailedAnalysis_HasNoViolations(int width)
    {
        await using var page = await shell.OpenSignedInAsync("/themen", width, prepare: browser =>
            Material.AnswerAsync(
                browser,
                documents: [new[] { Material.Document("Failed", "Die Analyse ist gerade nicht möglich. Versuch es gleich noch einmal.") }],
                topics: [Array.Empty<object>()]));
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync("hat nicht geklappt");
        await Assertions.Expect(page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Erneut analysieren" })).ToBeVisibleAsync();

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/themen with a failed analysis at {width} px");
    }

    // #307: Content's 429 says why in its title. The alert of the form shows the sentence, the text stays for later.
    [Fact]
    public async Task PasteAtALimit_OnAPhone_SaysWhy_AndKeepsTheText()
    {
        const string sentence = "Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.";
        await using var page = await shell.OpenSignedInAsync("/themen", WebShell.PhoneWidth, prepare: async browser =>
        {
            await Material.AnswerAsync(browser, documents: [Array.Empty<object>()], topics: [Array.Empty<object>()]);
            await browser.RouteAsync("**/api/content/documents", route => route.Request.Method == "POST"
                ? route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 429,
                    ContentType = "application/problem+json",
                    Body = $$"""{"status":429,"title":"{{sentence}}","traceId":"4bf92f3577b34da6a3ce929d0e0e4736"}""",
                })
                : route.FallbackAsync());
        });
        var text = page.Page.GetByLabel("Text", new PageGetByLabelOptions { Exact = true });
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync("Du hast noch keinen Text eingefügt.");

        await text.FillAsync("Die Photosynthese wandelt Lichtenergie in chemische Energie um.");
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Themen erkennen" }).ClickAsync();

        var alert = page.Page.Locator("form [role=alert]");
        await Assertions.Expect(alert.Locator("p")).ToHaveTextAsync(sentence);
        await Assertions.Expect(page.Page.Locator(".request-id")).ToHaveCountAsync(0);
        await Assertions.Expect(text).ToHaveValueAsync("Die Photosynthese wandelt Lichtenergie in chemische Energie um.");
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/themen at a limit at {WebShell.PhoneWidth} px");
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