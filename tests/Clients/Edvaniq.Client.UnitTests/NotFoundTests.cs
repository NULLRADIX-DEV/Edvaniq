using Bunit;

using Edvaniq.Client.UI.Pages;

namespace Edvaniq.Client.UnitTests;

// An address that does not exist ends on a page that says so and leads back to the start.
public sealed class NotFoundTests : BunitContext
{
    [Fact]
    public void SaysTheAddressIsUnknown_AndLeadsBackToTheStartPage()
    {
        var page = Render<NotFound>();

        Assert.Equal("Seite nicht gefunden", page.Find("h1").TextContent);
        var start = Assert.Single(page.FindAll("a"), link => link.TextContent == "Zur Startseite");
        Assert.Equal("", start.GetAttribute("href"));
    }
}