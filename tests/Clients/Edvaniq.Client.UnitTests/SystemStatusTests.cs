using System.Globalization;

using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// The page proves the way into the database: it shows what the database stored, or a hint with a way to try again.
public sealed class SystemStatusTests : BunitContext
{
    private static readonly PingResult Stored = new(42, new DateTimeOffset(2026, 10, 13, 9, 7, 14, TimeSpan.Zero));
    private static readonly PingResult? NotThrough = null;

    [Fact]
    public void OnOpen_ShowsThePing_AsTheDatabaseStoredIt()
    {
        var ping = new FakePing(Stored);

        var page = RenderPage(ping);

        Assert.Equal(1, ping.Pings);
        var status = page.Find("[role=status]").TextContent;
        Assert.Contains("Ping Nr. 42", status);
        Assert.Contains(Stored.PingedAt.ToLocalTime().ToString("dd.MM.yyyy 'um' HH:mm:ss", CultureInfo.InvariantCulture), status);
    }

    // Without a backend the shell's banner offers "Erneut versuchen" too. The ping's button says what it does, so a
    // screen reader's list of buttons tells them apart.
    [Fact]
    public void Failure_ShowsAHint_WithRetry()
    {
        var page = RenderPage(new FakePing(NotThrough));

        Assert.Contains("Der Ping ist nicht angekommen.", page.Find("[role=status]").TextContent);
        Assert.Equal("Ping erneut senden", page.Find("button").TextContent.Trim());
    }

    // A screen reader announces every change of the live region. The button stays outside of it, so it keeps the focus.
    [Fact]
    public void Button_StaysOutsideTheLiveRegion()
    {
        var page = RenderPage(new FakePing(NotThrough));

        Assert.Single(page.FindAll("button"));
        Assert.Empty(page.FindAll("[role=status] button"));
    }

    [Fact]
    public void Retry_AfterAFailure_ShowsThePing()
    {
        var ping = new FakePing(NotThrough, Stored);
        var page = RenderPage(ping);

        page.Find("button").Click();

        Assert.Equal(2, ping.Pings);
        var status = page.Find("[role=status]").TextContent;
        Assert.Contains("Ping Nr. 42", status);
        Assert.DoesNotContain("nicht angekommen", status);
        Assert.Equal("Noch einmal pingen", page.Find("button").TextContent.Trim());
    }

    // Until the first answer there is nothing to repeat, so there is no button yet.
    [Fact]
    public void FirstPing_WhileItRuns_SaysSo()
    {
        var ping = new FakePing(Stored) { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = RenderPage(ping);

        Assert.Contains("Ping läuft", page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll("button"));

        ping.Gate.SetResult();

        page.WaitForAssertion(() => Assert.Contains("Ping Nr. 42", page.Find("[role=status]").TextContent));
    }

    // Every ping writes to the database. A second click while one runs sends nothing. The status goes back to "läuft",
    // so a screen reader hears the answer again even when it is the same failure as before.
    [Fact]
    public void Retry_WhileAPingRuns_SendsNoSecondOne()
    {
        var ping = new FakePing(NotThrough);
        var page = RenderPage(ping);
        ping.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        page.Find("button").Click();
        page.Find("button").Click();

        Assert.Equal(2, ping.Pings);
        Assert.Contains("Ping läuft", page.Find("[role=status]").TextContent);

        ping.Gate.SetResult();

        page.WaitForAssertion(() => Assert.Contains("Der Ping ist nicht angekommen.", page.Find("[role=status]").TextContent));
    }

    // Only the browser can send the ping. While the page is prerendered on the server, there is no IPing at all.
    [Fact]
    public void WhilePrerendering_NothingIsSent()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var page = Render<SystemStatus>();

        Assert.Equal("Systemstatus", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("[role=status]"));
        Assert.Empty(page.FindAll("button"));
    }

    private IRenderedComponent<SystemStatus> RenderPage(IPing ping)
    {
        Services.AddSingleton(ping);
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<SystemStatus>();
    }
}