using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// The shell has to work with the keyboard alone: a skip link to the content, a menu button that says whether the
// navigation is open, Escape to close it again.
public sealed class ShellLayoutTests : BunitContext
{
    private FakeBackendStatus backend = new(true);

    public ShellLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IBackendStatus>(_ => backend);
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    [Fact]
    public void SkipLink_ComesFirst_AndLeadsToTheContent()
    {
        var shell = RenderShell();

        var skipLink = shell.Find("a");
        Assert.Equal("Zum Inhalt springen", skipLink.TextContent);
        Assert.Equal("#main", skipLink.GetAttribute("href"));
        Assert.Equal("-1", shell.Find("main#main").GetAttribute("tabindex"));
    }

    // With <base href="/"> a bare "#main" would lead from any page back to the start page.
    [Fact]
    public void SkipLink_StaysOnTheCurrentPage()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/plan?tag=heute#oben");

        var shell = RenderShell();

        Assert.Equal("plan?tag=heute#main", shell.Find("a.skip-link").GetAttribute("href"));
    }

    [Fact]
    public void MenuButton_OpensAndClosesTheNavigation()
    {
        var shell = RenderShell();
        var button = shell.Find("button.shell-menu-button");
        Assert.Equal("shell-nav", button.GetAttribute("aria-controls"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.DoesNotContain("open", shell.Find("nav#shell-nav").ClassList);

        button.Click();

        Assert.Equal("true", shell.Find("button.shell-menu-button").GetAttribute("aria-expanded"));
        Assert.Contains("open", shell.Find("nav#shell-nav").ClassList);

        shell.Find("button.shell-menu-button").Click();

        Assert.Equal("false", shell.Find("button.shell-menu-button").GetAttribute("aria-expanded"));
    }

    // Right after opening, the focus is still on the menu button, so Escape has to work there too.
    [Theory]
    [InlineData("button.shell-menu-button")]
    [InlineData("nav#shell-nav a")]
    public void Escape_ClosesTheMenu_AndPutsTheFocusBackOnTheButton(string focused)
    {
        var shell = RenderShell();
        shell.Find("button.shell-menu-button").Click();

        shell.Find(focused).KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("false", shell.Find("button.shell-menu-button").GetAttribute("aria-expanded"));
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void Navigating_ClosesTheMenu()
    {
        var shell = RenderShell();
        shell.Find("button.shell-menu-button").Click();

        Services.GetRequiredService<NavigationManager>().NavigateTo("/plan");

        shell.WaitForAssertion(() =>
            Assert.Equal("false", shell.Find("button.shell-menu-button").GetAttribute("aria-expanded")));
    }

    [Fact]
    public void CurrentPage_IsMarkedForScreenReaders()
    {
        var shell = RenderShell();

        var start = shell.Find("nav#shell-nav a");
        Assert.Equal("page", start.GetAttribute("aria-current"));
        Assert.Contains("active", start.ClassList);
    }

    [Fact]
    public void WhilePrerendering_NothingIsChecked()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var shell = RenderShell();

        Assert.Equal(0, backend.Checks);
        Assert.Empty(shell.FindAll("[role=status]"));
    }

    [Fact]
    public void InTheBrowser_TheBackendIsChecked()
    {
        RenderShell();

        Assert.Equal(1, backend.Checks);
    }

    // On a phone the runtime may take seconds to load. A menu button would do nothing until then, so there is none and
    // the navigation stays reachable.
    [Fact]
    public void WhilePrerendering_TheNavigationIsOpenWithoutAButton()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var shell = RenderShell();

        Assert.Empty(shell.FindAll("button.shell-menu-button"));
        Assert.Contains("open", shell.Find("nav#shell-nav").ClassList);
    }

    [Fact]
    public void AfterASuccessfulRetry_TheFocusMovesToTheContent()
    {
        backend = new FakeBackendStatus(false, true);
        var shell = RenderShell();

        shell.Find(".backend-banner button").Click();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    private IRenderedComponent<ShellLayout> RenderShell() =>
        Render<ShellLayout>(parameters => parameters
            .Add(layout => layout.Body, (RenderFragment)(builder => builder.AddMarkupContent(0, "<h1>Seite</h1>"))));
}