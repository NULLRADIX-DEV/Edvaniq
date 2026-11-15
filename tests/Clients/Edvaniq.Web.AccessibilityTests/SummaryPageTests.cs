using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #346: the summary of a topic on a phone and on a desktop, only with the keyboard: the standard summary comes after a
// moment of loading, Tab and Enter switch the length, nothing scrolls sideways, and each state passes axe-core.
public sealed partial class SummaryPageTests(WebShell shell)
{
    // WCAG 2.5.5 asks for 44 CSS pixels.
    private const float SmallestTarget = 44;

    private static string Path => $"/themen/{Material.TopicId}/zusammenfassung";

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task SwitchingTheLength_WithTheKeyboardOnly(int width)
    {
        await using var page = await shell.OpenSignedInAsync(Path, width, prepare: AnswerSummariesAsync);

        await Assertions.Expect(page.Page.Locator("h1")).ToHaveTextAsync("Zusammenfassung zu „Photosynthese“");
        await Assertions.Expect(page.Page.Locator(".summary-text p").First).ToHaveTextAsync("Die Photosynthese wandelt Licht in chemische Energie um.");
        var standard = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Standard" });
        await Assertions.Expect(standard).ToHaveAttributeAsync("aria-pressed", "true");
        foreach (var name in new[] { "Kurz", "Standard", "Detailliert" })
        {
            await AssertLargeEnoughAsync(page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name }));
        }

        await Assertions.Expect(page.Page.Locator(".summary-meta .ai-note")).ToContainTextAsync("KI kann Fehler machen.");
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with the standard summary at {width} px");

        var shortOne = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Kurz" });
        await TabToAsync(page.Page, shortOne);
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(shortOne).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(shortOne).ToBeFocusedAsync();
        await Assertions.Expect(page.Page.Locator(".summary-text p")).ToHaveTextAsync(["Kurz gesagt: Licht wird zu Zucker."]);
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} with the short summary at {width} px");

        await page.Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(standard).ToBeFocusedAsync();
        await page.Page.Keyboard.PressAsync("Space");
        await Assertions.Expect(page.Page.Locator(".summary-text p").First).ToHaveTextAsync("Die Photosynthese wandelt Licht in chemische Energie um.");
    }

    // The loading state while the summary is made passes axe-core too.
    [Fact]
    public async Task WhileWriting_ShowsTheLoadingState_WithoutViolations()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: async browser =>
        {
            await Material.AnswerAsync(browser, documents: [new[] { Material.Document("Ready") }], topics: [Material.Topics]);
            await browser.RouteAsync(SummaryRoute(), route => FulfillAsync(route, Summary(LengthOf(route.Request.Url), "pending", null), 202));
        });

        await Assertions.Expect(page.Page.Locator("[role=status]").Filter(new LocatorFilterOptions { HasText = "Zusammenfassung wird erstellt" }))
            .ToHaveCountAsync(1);
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} while writing at {WebShell.PhoneWidth} px");
    }

    // POST of the standard one is pending first and ready on the next read, the short one is ready at once.
    private static async Task AnswerSummariesAsync(IPage browser)
    {
        await Material.AnswerAsync(browser, documents: [new[] { Material.Document("Ready") }], topics: [Material.Topics]);
        await browser.RouteAsync(SummaryRoute(), route =>
        {
            var length = LengthOf(route.Request.Url);
            var text = length == "short"
                ? "Kurz gesagt: Licht wird zu Zucker."
                : "Die Photosynthese wandelt Licht in chemische Energie um.\nSie läuft in den Chloroplasten ab.";
            return route.Request.Method == "POST" && length == "standard"
                ? FulfillAsync(route, Summary(length, "pending", null), 202)
                : FulfillAsync(route, Summary(length, "ready", text));
        });
    }

    private static Regex SummaryRoute() => SummaryPath();

    private static string LengthOf(string url) => url[(url.LastIndexOf('/') + 1)..];

    private static object Summary(string length, string status, string? text) => new
    {
        topicId = Material.TopicId,
        length,
        status,
        text,
        failureCode = (string?)null,
        aiGenerated = true,
        model = (string?)null,
        promptVersion = (string?)null,
        createdAt = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc),
        readyAt = (DateTime?)null,
        sources = Array.Empty<object>(),
    };

    private static Task FulfillAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        });

    // Presses Tab until the element has the focus, at most 30 times.
    private static async Task TabToAsync(IPage page, ILocator target)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.Fail("Tab never reached the button.");
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

    [GeneratedRegex("/api/content/topics/[0-9a-f-]+/summaries/(short|standard|detailed)$")]
    private static partial Regex SummaryPath();
}