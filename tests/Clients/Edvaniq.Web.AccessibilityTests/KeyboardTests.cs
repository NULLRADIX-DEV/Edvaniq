using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// What axe cannot see is how the shell behaves for someone who never touches the mouse.
public sealed class KeyboardTests(WebShell shell)
{
    private const string FocusedElement = """
        () => {
            const element = document.activeElement;
            return element === null || element === document.body ? "" : element.tagName.toLowerCase() + (element.id ? "#" + element.id : "");
        }
        """;

    private const string Stops = """
        [...document.querySelectorAll("a[href], button, input, select, textarea, [tabindex]")]
            .filter(element => element.tabIndex >= 0 && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== "hidden")
        """;

    private const string FocusStops = "() => " + Stops + ".length";

    // The position of the focused element among the stops, -1 while the focus is outside the page.
    private const string FocusedStop = "() => " + Stops + ".indexOf(document.activeElement)";

    private const string FirstStop = """
        () => {
            const element = [...document.querySelectorAll("a[href], button, input, select, textarea, [tabindex]")]
                .find(element => element.tabIndex >= 0 && !element.disabled);
            return element === undefined ? "" : element.tagName.toLowerCase() + "." + element.classList[0];
        }
        """;

    private const string FocusRing = """
        () => {
            const style = getComputedStyle(document.activeElement);
            return style.outlineStyle !== "none" && parseFloat(style.outlineWidth) >= 2;
        }
        """;

    [Fact]
    public async Task SkipLink_IsTheFirstStop_AndMovesFocusToMain()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/systemstatus", WebShell.DesktopWidth);

        // Where the very first Tab lands depends on whether FocusOnNavigate has put the focus on the heading yet, so
        // the order comes from the page and the link gets the focus directly.
        Assert.Equal("a.skip-link", await page.Page.EvaluateAsync<string>(FirstStop));
        await page.Page.Locator(".skip-link").FocusAsync();
        await Assertions.Expect(page.Page.Locator(".skip-link")).ToHaveTextAsync("Zum Inhalt springen");

        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("#main")).ToBeFocusedAsync();
        Assert.Equal("main#main", await page.Page.EvaluateAsync<string>(FocusedElement));
    }

    [Fact]
    public async Task Menu_OpensAndClosesWithTheKeyboard()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/", WebShell.PhoneWidth);
        var button = page.Page.Locator(".shell-menu-button");
        var nav = page.Page.Locator("#shell-nav");

        await TabToAsync(page.Page, button);
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(nav).ToBeHiddenAsync();

        await page.Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(nav).ToBeVisibleAsync();

        await page.Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(nav.GetByRole(AriaRole.Link).First).ToBeFocusedAsync();

        await page.Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(nav).ToBeHiddenAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();

        await page.Page.Keyboard.PressAsync("Space");
        await Assertions.Expect(nav).ToBeVisibleAsync();
        await page.Page.Keyboard.PressAsync("Space");
        await Assertions.Expect(nav).ToBeHiddenAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task EveryStop_ShowsWhereTheFocusIs(int width)
    {
        await using var page = await shell.OpenAsync(shell.Address, "/", width);
        var stops = await page.Page.EvaluateAsync<int>(FocusStops);
        Assert.True(stops >= 3, $"Expected at least the skip link, the brand and a navigation entry, found {stops} stops");

        // FocusOnNavigate may already have put the focus on the heading. Then Tab goes on from there to the end of the
        // page, once out to the browser and round again from the top. One press more than there are stops reaches every
        // stop either way.
        var reached = new HashSet<int>();
        for (var press = 1; press <= stops + 1; press++)
        {
            await page.Page.Keyboard.PressAsync("Tab");

            var stop = await page.Page.EvaluateAsync<int>(FocusedStop);
            if (stop < 0)
            {
                continue;
            }

            reached.Add(stop);
            var element = await page.Page.EvaluateAsync<string>(FocusedElement);
            Assert.True(await page.Page.EvaluateAsync<bool>(FocusRing), $"Stop {stop + 1} of {stops} ({element}) shows no focus ring");
        }

        Assert.True(reached.Count == stops, $"Tab reached {reached.Count} of {stops} stops in one round");
    }

    // Presses Tab until the element has the focus, so a new stop in front of it does not break the test.
    private static async Task TabToAsync(IPage page, ILocator target)
    {
        for (var press = 0; press < 10; press++)
        {
            await page.Keyboard.PressAsync("Tab");
            if (await target.EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }
        }

        Assert.Fail("The element did not get the focus within 10 presses of Tab");
    }
}