namespace Edvaniq.Web.AccessibilityTests;

// The visitor's browser talks to the web host and nobody else: no tracker, no font or script from another server. The
// privacy notice promises that, so every page is loaded until the client runs and nothing it requested may leave the
// host. A link to another site, like the imprint, is not a request until someone follows it.
public sealed class ThirdPartyTests(WebShell shell)
{
    public static TheoryData<string> Pages { get; } = ["/", "/systemstatus", "/datenschutz", "/open-source", "/there-is-no-such-page"];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_LoadsNothingFromAnotherHost(string path)
    {
        await using var page = await shell.OpenAsync(shell.Address, path, WebShell.DesktopWidth);

        var host = new Uri(shell.Address).Authority;
        var elsewhere = page.Requests
            .Select(url => new Uri(url))
            .Where(url => url.Scheme is "http" or "https" && url.Authority != host)
            .ToList();

        Assert.NotEmpty(page.Requests);
        Assert.Empty(elsewhere);
    }
}