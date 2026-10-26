using Bunit;

using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

namespace Edvaniq.Client.UnitTests;

// Until the first real users the privacy notice is a draft and says so right under the heading. It names who is
// responsible and leads to the imprint for the address.
public sealed class PrivacyTests : BunitContext
{
    [Fact]
    public void Notice_SaysFirstThatItIsADraft()
    {
        var page = Render<Privacy>();

        var note = page.Find("h1 + [role=note]");
        Assert.StartsWith("Vorläufige Fassung für den Testbetrieb", note.TextContent);
    }

    [Fact]
    public void Notice_NamesTheControllerAndLeadsToTheImprint()
    {
        var page = Render<Privacy>();

        Assert.Equal("tristan@nullradix.de", page.Find("a[href='mailto:tristan@nullradix.de']").TextContent);
        Assert.Equal("Impressum", page.Find($"a[href='{Imprint.Url}']").TextContent);
    }
}