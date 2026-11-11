using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// With reduced motion asked for, nothing on a page moves, not even an element that brings its own animation. The same
// element has to move without it, or the check would pass on a page that never animates anyway.
public sealed class ReducedMotionTests(WebShell shell)
{
    private const string Probe = "motion-probe";

    private const string AddProbe = """
        id => {
            const probe = document.createElement("p");
            probe.id = id;
            probe.textContent = "Probe";
            probe.style.transition = "opacity 1s";
            probe.style.animation = id + " 1s infinite";
            document.body.append(probe);
        }
        """;

    // Every element and pseudo-element with an animation or a transition that takes time.
    private const string Moving = """
        () => {
            const moves = style =>
                style.animationName.split(",").some(name => name.trim() !== "none")
                || style.transitionDuration.split(",").some(duration => parseFloat(duration) > 0);
            const name = element => element.tagName.toLowerCase() + (element.id ? "#" + element.id : "") + (element.classList[0] ? "." + element.classList[0] : "");
            return [...document.querySelectorAll("*")].flatMap(element =>
                [null, "::before", "::after"]
                    .filter(pseudo => moves(getComputedStyle(element, pseudo)))
                    .map(pseudo => name(element) + (pseudo ?? "")));
        }
        """;

    [Theory]
    [MemberData(nameof(ShellScanTests.Pages), MemberType = typeof(ShellScanTests))]
    public async Task Page_StandsStill_WhenReducedMotionIsAskedFor(string path, int width)
    {
        await using var page = await shell.OpenAsync(shell.Address, path, width);

        await AssertStandsStillAsync(page.Page, path, width);
    }

    [Theory]
    [MemberData(nameof(ShellScanTests.SignedInPages), MemberType = typeof(ShellScanTests))]
    public async Task SignedInPage_StandsStill_WhenReducedMotionIsAskedFor(string path, int width)
    {
        await using var page = await shell.OpenSignedInAsync(path, width);

        await AssertStandsStillAsync(page.Page, path, width);
    }

    private static async Task AssertStandsStillAsync(IPage page, string path, int width)
    {
        await page.EvaluateAsync(AddProbe, Probe);

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        var moving = await page.EvaluateAsync<string[]>(Moving);
        Assert.True(moving.Length == 0, $"Moving on {path} at {width} px with reduced motion: {string.Join(", ", moving)}");

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.NoPreference });
        Assert.Contains("p#" + Probe, await page.EvaluateAsync<string[]>(Moving));
    }
}