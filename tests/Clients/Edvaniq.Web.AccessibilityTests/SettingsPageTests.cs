using System.Text.Json;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #512 AC4: the daily budget is set on a phone with the keyboard alone. The field is large enough for the thumb, an error
// takes the focus to it, nothing scrolls sideways, and axe-core finds nothing in any state.
public sealed class SettingsPageTests(WebShell shell)
{
    private const string Path = "/einstellungen";

    // WCAG 2.5.5 asks for 44 CSS pixels.
    private const float SmallestTarget = 44;

    [Fact]
    public async Task KeyboardAlone_SetsTheBudget_OnAPhone()
    {
        var sent = new List<string>();
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser => AnswerAsync(browser, sent));
        var field = page.Page.GetByLabel("Lernzeit pro Tag (Minuten)");
        await Assertions.Expect(field).ToHaveValueAsync("60");
        await CheckAsync(page.Page, "loaded");
        await AssertLargeEnoughAsync(field);
        await AssertLargeEnoughAsync(page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Speichern" }));

        await field.FocusAsync();
        await page.Page.Keyboard.PressAsync("Control+A");
        await page.Page.Keyboard.TypeAsync("800");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("#budget-minutes-error")).ToContainTextAsync("zwischen 1 und 720 Minuten");
        await Assertions.Expect(field).ToBeFocusedAsync();
        Assert.Empty(sent);
        await CheckAsync(page.Page, "an error");

        await page.Page.Keyboard.PressAsync("Control+A");
        await page.Page.Keyboard.TypeAsync("45");
        await page.Page.Keyboard.PressAsync("Tab");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("[role=status] .budget-saved")).ToContainTextAsync("45 Minuten");
        await Assertions.Expect(page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Zum Plan für heute" })).ToBeVisibleAsync();
        await CheckAsync(page.Page, "saved");
        using var week = JsonDocument.Parse(Assert.Single(sent));
        Assert.Equal(7, week.RootElement.EnumerateObject().Count());
        Assert.All(week.RootElement.EnumerateObject(), day => Assert.Equal(45, day.Value.GetInt32()));
    }

    // Planning as the page sees it: 60 minutes by default, a stored week comes back as sent.
    private static Task AnswerAsync(IPage page, List<string> sent) =>
        page.RouteAsync("**/api/planning/daily-budget", route =>
        {
            var body = route.Request.Method == "PUT" ? route.Request.PostData ?? "{}" : null;
            if (body is not null)
            {
                sent.Add(body);
            }

            var minutes = body is null ? 60 : JsonDocument.Parse(body).RootElement.GetProperty("monday").GetInt32();
            return route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    monday = minutes,
                    tuesday = minutes,
                    wednesday = minutes,
                    thursday = minutes,
                    friday = minutes,
                    saturday = minutes,
                    sunday = minutes,
                    isDefault = body is null,
                }),
            });
        });

    private static async Task AssertLargeEnoughAsync(ILocator target)
    {
        var box = await target.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Height >= SmallestTarget, $"The target is {box.Height} px high.");
        Assert.True(box.Width >= SmallestTarget, $"The target is {box.Width} px wide.");
    }

    private static async Task CheckAsync(IPage page, string state)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways at {state}.");
        await AxeScan.AssertNoViolationsAsync(page, $"{Path} at {state}");
    }
}