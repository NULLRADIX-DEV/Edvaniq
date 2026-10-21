using Bunit;

using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

namespace Edvaniq.Client.UnitTests;

// After an unexpected error the user gets a plain message, a way to try again and a way back to the start, and the id
// of the request to pass on. Nothing about the inside: the page never gets more than the id and an address.
public sealed class ServerErrorTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public void ShowsAPlainMessage_InGerman()
    {
        var page = RenderPage(TraceId, "/systemstatus");

        Assert.Equal("Etwas ist schiefgelaufen", page.Find("h1").TextContent);
        Assert.Contains("Versuch es gleich noch einmal.", page.Find("p").TextContent);
    }

    // The retry asks for the page that failed. It is a link, so it works before the runtime has loaded, and it fetches
    // the page whole instead of an enhanced navigation.
    [Fact]
    public void Retry_IsALinkToThePageThatFailed()
    {
        var page = RenderPage(TraceId, "/systemstatus?seite=2");

        var retry = page.Find("a.button");
        Assert.Equal("Erneut versuchen", retry.TextContent);
        Assert.Equal("/systemstatus?seite=2", retry.GetAttribute("href"));
        Assert.Equal("false", retry.GetAttribute("data-enhance-nav"));
    }

    [Fact]
    public void LeadsBackToTheStartPage()
    {
        var page = RenderPage(TraceId, "/systemstatus");

        var start = Assert.Single(page.FindAll("a"), link => link.TextContent == "Zur Startseite");
        Assert.Equal("", start.GetAttribute("href"));
    }

    [Fact]
    public void NamesTheRequest()
    {
        var page = RenderPage(TraceId, "/");

        Assert.Equal(TraceId, page.Find(".request-id code").TextContent);
        Assert.Contains("Kennung", page.Markup);
    }

    // Without a trace there is nothing to pass on, so there is no hint asking for it either.
    [Fact]
    public void WithoutATrace_NoIdIsShown()
    {
        var page = RenderPage(traceId: null, "/");

        Assert.Empty(page.FindAll(".request-id"));
        Assert.DoesNotContain("Kennung", page.Markup);
        Assert.Equal("Erneut versuchen", page.Find("a.button").TextContent);
    }

    // The id is written as text. Whatever a caller hands in cannot become markup.
    [Fact]
    public void TheId_IsNeverMarkup()
    {
        var page = RenderPage("<b>fett</b>", "/");

        Assert.Empty(page.FindAll(".request-id b"));
        Assert.Equal("<b>fett</b>", page.Find(".request-id code").TextContent);
    }

    [Fact]
    public void RequestId_WithoutAValue_RendersNothing()
    {
        var empty = Render<RequestId>();

        Assert.Empty(empty.Markup);
    }

    private IRenderedComponent<ServerError> RenderPage(string? traceId, string retryHref) =>
        Render<ServerError>(parameters => parameters
            .Add(page => page.TraceId, traceId)
            .Add(page => page.RetryHref, retryHref));
}