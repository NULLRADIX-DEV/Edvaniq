using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Shell;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// Without a backend the shell shows a hint with a way to try again, instead of pages that silently stay empty.
public sealed class BackendStatusBannerTests : BunitContext
{
    [Fact]
    public void Reachable_ShowsNoHint()
    {
        var banner = RenderBanner(new FakeBackendStatus(true));

        Assert.Empty(banner.Find("[role=status]").Children);
    }

    [Fact]
    public void Unreachable_ShowsTheHintWithRetry()
    {
        var banner = RenderBanner(new FakeBackendStatus(false));

        Assert.Contains("Keine Verbindung zum Server.", banner.Find("[role=status]").TextContent);
        Assert.Equal("Erneut versuchen", banner.Find("button").TextContent.Trim());
    }

    // The button that had the focus disappears, so the shell gets told and a screen reader hears the all-clear.
    [Fact]
    public void Retry_WhenTheBackendIsBack_RemovesTheHint_AndSaysSo()
    {
        var backend = new FakeBackendStatus(false, true);
        var reconnected = 0;
        Services.AddSingleton<IBackendStatus>(backend);
        var banner = Render<BackendStatusBanner>(parameters => parameters
            .Add(component => component.OnReconnected, () => reconnected++));

        banner.Find("button").Click();

        Assert.Equal(2, backend.Checks);
        Assert.Empty(banner.FindAll("button"));
        Assert.Equal("Die Verbindung zum Server ist wieder da.", banner.Find("[role=status] .visually-hidden").TextContent);
        Assert.Equal(1, reconnected);
    }

    [Fact]
    public void FirstCheck_WhenReachable_AnnouncesNothing()
    {
        var reconnected = 0;
        Services.AddSingleton<IBackendStatus>(new FakeBackendStatus(true));

        Render<BackendStatusBanner>(parameters => parameters
            .Add(component => component.OnReconnected, () => reconnected++));

        Assert.Equal(0, reconnected);
    }

    [Fact]
    public void Retry_WhileStillDown_KeepsTheHint()
    {
        var backend = new FakeBackendStatus(false);
        var banner = RenderBanner(backend);

        banner.Find("button").Click();

        Assert.Equal(2, backend.Checks);
        Assert.Contains("Keine Verbindung zum Server.", banner.Find("[role=status]").TextContent);
    }

    private IRenderedComponent<BackendStatusBanner> RenderBanner(IBackendStatus backend)
    {
        Services.AddSingleton(backend);
        return Render<BackendStatusBanner>();
    }
}