using Xunit.Sdk;

namespace Edvaniq.Web.AccessibilityTests;

// The scan only helps if it bites. This page breaks three rules on purpose: an image without alternative text, a field
// without a label and text that is too faint. If the scan stops reporting them, the shell's green result means nothing.
public sealed class AxeScanTests(WebShell shell)
{
    private const string PageWithViolations = """
        <!DOCTYPE html>
        <html lang="en">
        <head><title>Known violations</title></head>
        <body>
          <main>
            <h1>Known violations</h1>
            <img id="logo" src="data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=" width="40" height="40">
            <input id="name" type="text">
            <p id="faint" style="color: #bbbbbb; background: #ffffff">Hard to read</p>
          </main>
        </body>
        </html>
        """;

    [Fact]
    public async Task KnownViolations_AreFound()
    {
        await using var page = await shell.OpenHtmlAsync(PageWithViolations);

        var violations = await AxeScan.ViolationsAsync(page.Page);

        Assert.Contains("image-alt", violations.Select(violation => violation.Id));
        Assert.Contains("label", violations.Select(violation => violation.Id));
        Assert.Contains("color-contrast", violations.Select(violation => violation.Id));
    }

    [Fact]
    public async Task KnownViolations_FailTheTest_WithAReadableList()
    {
        await using var page = await shell.OpenHtmlAsync(PageWithViolations);

        var failure = await Assert.ThrowsAsync<FailException>(
            () => AxeScan.AssertNoViolationsAsync(page.Page, "the page with known violations"));

        Assert.Contains("on the page with known violations", failure.Message);
        Assert.Contains("image-alt (critical)", failure.Message);
        Assert.Contains("label (critical)", failure.Message);
        Assert.Contains("color-contrast (serious)", failure.Message);
        Assert.Contains("#logo", failure.Message);
        Assert.Contains("#name", failure.Message);
        Assert.Contains("#faint", failure.Message);
        Assert.Contains("https://dequeuniversity.com/rules/axe/", failure.Message);
    }

    [Fact]
    public async Task PageWithoutViolations_Passes()
    {
        await using var page = await shell.OpenHtmlAsync(
            """
            <!DOCTYPE html>
            <html lang="en">
            <head><title>Fine</title></head>
            <body><main><h1>Fine</h1><label>Name <input type="text"></label></main></body>
            </html>
            """);

        await AxeScan.AssertNoViolationsAsync(page.Page, "a fine page");
    }
}