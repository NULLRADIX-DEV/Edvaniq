using Bunit;

using Edvaniq.Client.UI.OpenSource;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;

namespace Edvaniq.Client.UnitTests;

// The page shows the license list as maintained in docs/licenses.md, one table per kind of component, and links the
// text of each license.
public sealed class OpenSourceTests : BunitContext
{
    private const string Markdown = """
        # Lizenzen

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | `Not.Listed` | MIT | above the list |

        ## Liste

        ### NuGet-Pakete

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | `Yarp.ReverseProxy` | MIT | Gateway, siehe [Abschnitt](#abschnitt) |
        | `MySql.Data` | GPL-2.0-only WITH Universal-FOSS-exception-1.0 | Vorläufige Ausnahme: DB |

        ### Tools und Programme

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | Grype (`anchore/grype`) | Apache-2.0 | Scan |
        | `Microsoft.WindowsAppSDK` | Lizenzbedingungen von Microsoft | Einzelfreigabe: App |
        | `Polly.Core` | (MIT OR Apache-2.0) | Resilience |

        ## Vorläufige Ausnahmen

        | `Not.A.Row` | GPL-3.0-only | below the list |
        """;

    [Fact]
    public void EmbeddedList_IsTheMaintainedLicenseList()
    {
        var maintained = File.ReadAllText(Path.Combine(Repository.Root, "docs", "licenses.md"));

        Assert.Equal(maintained.ReplaceLineEndings("\n"), LicenseNotices.EmbeddedList().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Read_GroupsTheRowsUnderTheirHeading()
    {
        var notices = LicenseNotices.Read(Markdown);

        Assert.Equal(["NuGet-Pakete", "Tools und Programme"], notices.Groups.Select(group => group.Title));
        Assert.Equal(
            [new LicenseNotices.Entry("Yarp.ReverseProxy", "MIT"), new LicenseNotices.Entry("MySql.Data", "GPL-2.0-only WITH Universal-FOSS-exception-1.0")],
            notices.Groups[0].Entries);
        Assert.Equal("Grype (anchore/grype)", notices.Groups[1].Entries[0].Component);
    }

    [Fact]
    public void SpdxIds_LeaveOutLicensesThatAreNoExpression()
    {
        var notices = LicenseNotices.Read(Markdown);

        Assert.Equal(["MIT", "GPL-2.0-only", "Universal-FOSS-exception-1.0", "Apache-2.0"], notices.SpdxIds);
    }

    [Fact]
    public void Page_ShowsEveryRowOfTheLicenseList()
    {
        var page = Render<OpenSource>();

        var rows = page.FindAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent).ToList()).ToList();
        Assert.Equal(LicenseNotices.Embedded.Groups.Sum(group => group.Entries.Count), rows.Count);
        Assert.Equal(TableRowsOf(File.ReadAllText(Path.Combine(Repository.Root, "docs", "licenses.md"))), rows.Count);
        Assert.Contains(rows, row => row.SequenceEqual(["Yarp.ReverseProxy", "MIT"]));
    }

    // A screen reader names each table by its caption and reads the column header with every cell.
    [Fact]
    public void EachTable_HasACaptionAndColumnHeaders()
    {
        var page = Render<OpenSource>();

        var tables = page.FindAll("table");
        Assert.Equal(LicenseNotices.Embedded.Groups.Select(group => group.Title), tables.Select(table => table.QuerySelector("caption")!.TextContent));
        Assert.All(tables, table => Assert.Equal(
            [("Komponente", "col"), ("Lizenz", "col")],
            table.QuerySelectorAll("th").Select(header => (header.TextContent, header.GetAttribute("scope") ?? ""))));
    }

    [Fact]
    public void Page_LinksTheTextOfEachLicense()
    {
        var page = Render<OpenSource>();

        var links = page.FindAll(".license-texts a").Select(link => (link.TextContent, link.GetAttribute("href") ?? "")).ToList();
        Assert.Contains(("MIT", "https://spdx.org/licenses/MIT.html"), links);
        Assert.Equal(LicenseNotices.Embedded.SpdxIds.Count, links.Count);
    }

    // The page holds nothing that needs the browser, so the prerendered page is already complete.
    [Fact]
    public void WhilePrerendering_ThePageIsComplete()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var page = Render<OpenSource>();

        Assert.Equal("Open-Source-Hinweise", page.Find("h1").TextContent);
        Assert.NotEmpty(page.FindAll("tbody tr"));
    }

    // The rows of the tables under "## Liste", counted straight from the file.
    private static int TableRowsOf(string markdown) =>
        markdown.Split('\n').Select(line => line.Trim())
            .SkipWhile(line => line != "## Liste").Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Count(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| Komponente |", StringComparison.Ordinal));
}