using AngleSharp.Dom;

using Bunit;

using Edvaniq.Client.TestData;
using Edvaniq.Client.UI.Markdown;

using Markdig;

namespace Edvaniq.Client.UnitTests;

// Markdown from the user or the AI: what it may format shows formatted, nothing in it runs, and whatever the renderer
// doesn't know shows as the characters it was written with.
public sealed class MarkdownTests
{
    private static readonly string[] Schemes = ["https://", "http://", "mailto:"];

    public static TheoryData<string> Hostile => [.. HostileText.Payloads, .. HostileText.MarkdownPayloads];

    public static TheoryData<string> Plain => [.. HostileText.Payloads];

    // AC1: in a block and in a line, no element, attribute or link the text asks for.
    [Theory]
    [MemberData(nameof(Hostile))]
    public void HostileText_RunsNothing(string text)
    {
        AssertHarmless(Render(text));
        AssertHarmless(Render(text, inline: true));
    }

    // AC1: raw HTML is not Markdown, it reads exactly as written.
    [Theory]
    [MemberData(nameof(Plain))]
    public void RawHtml_ShowsAsText(string text)
    {
        var block = Render(text);
        Assert.Equal(["DIV", "P"], block.Find("div").DescendantsAndSelf<IElement>().Select(element => element.TagName));
        Assert.Equal(text, block.Find(".markdown").TextContent);
        Assert.Equal(text, TextOf(Render(text, inline: true)));
    }

    [Fact]
    public void HtmlBlock_ShowsAsText()
    {
        var page = Render("Vorher\n\n<script>window.__xss = 20</script>\n\nNachher");

        Assert.Equal(["Vorher", "<script>window.__xss = 20</script>", "Nachher"], page.FindAll("p").Select(p => p.TextContent));
    }

    [Theory]
    [InlineData("[Doku](javascript:alert(1))", "Doku")]
    [InlineData("[Doku](data:text/html,x)", "Doku")]
    [InlineData("[Doku](/themen)", "Doku")]
    [InlineData("[Doku](ftp://example.org/a)", "Doku")]
    [InlineData("[Doku](java&#115;cript:alert(1))", "Doku")]
    [InlineData("<javascript:alert(1)>", "javascript:alert(1)")]
    public void UnsafeLink_IsOnlyItsText(string text, string shown)
    {
        var page = Render(text);

        Assert.Empty(page.FindAll("a"));
        Assert.Equal(shown, page.Find("p").TextContent);
    }

    // External links open in a new tab, without access to this page or its address.
    [Theory]
    [InlineData("[Doku](https://example.org/a?b=1)", "https://example.org/a?b=1")]
    [InlineData("[Doku](http://example.org/)", "http://example.org/")]
    [InlineData("<https://example.org/a>", "https://example.org/a")]
    public void WebLink_OpensSafelyInNewTab(string text, string href)
    {
        var link = Render(text).Find("a");

        Assert.Equal(href, link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer nofollow", link.GetAttribute("rel"));
        Assert.Equal(" (öffnet in neuem Tab)", link.QuerySelector(".visually-hidden")!.TextContent);
    }

    [Fact]
    public void MailLink_StaysInTab()
    {
        var link = Render("<lehrer@example.org>").Find("a");

        Assert.Equal("mailto:lehrer@example.org", link.GetAttribute("href"));
        Assert.Null(link.GetAttribute("target"));
    }

    // AC1: an image would call a foreign host, its description stands in for it.
    [Fact]
    public void Image_IsOnlyItsDescription()
    {
        var page = Render("Sieh: ![Eine *Zelle*](https://example.org/zelle.png)");

        Assert.Empty(page.FindAll("img"));
        Assert.Equal("Sieh: Eine Zelle", page.Find("p").TextContent);
    }

    // AC2: headings sit below the page's own and never go past h6.
    [Fact]
    public void Headings_MoveBelowThePage()
    {
        var page = Render("# Eins\n\n## Zwei\n\n###### Sechs", headingOffset: 2);

        Assert.Equal(["H3", "H4", "H6"], page.FindAll("h1, h2, h3, h4, h5, h6").Select(heading => heading.TagName));
        Assert.Equal(["Eins", "Zwei", "Sechs"], page.FindAll("h3, h4, h6").Select(heading => heading.TextContent));
    }

    // AC2
    [Fact]
    public void Lists_NestAndKeepTheirStart()
    {
        var page = Render("- Eins\n  - Unterpunkt\n- Zwei\n\n3. Drei\n4. Vier");

        var outer = page.Find(".markdown > ul");
        Assert.Equal(2, outer.Children.Length);
        Assert.Equal("Unterpunkt", outer.QuerySelector("li > ul > li")!.TextContent);
        Assert.Empty(outer.QuerySelectorAll("p"));
        var ordered = page.Find("ol");
        Assert.Equal("3", ordered.GetAttribute("start"));
        Assert.Equal(["Drei", "Vier"], ordered.Children.Select(item => item.TextContent));
    }

    [Fact]
    public void LooseList_KeepsParagraphs()
    {
        var page = Render("- Eins\n\n- Zwei");

        Assert.Equal(["Eins", "Zwei"], page.FindAll("li > p").Select(p => p.TextContent));
    }

    // AC2: header cells name their column for screen readers, the table scrolls on its own.
    [Fact]
    public void Table_HasHeaderAndBody()
    {
        var page = Render("| Stoff | pH-Wert |\n|:---|---:|\n| Zitronensaft | **2** |\n| Wasser | 7 |");

        var region = page.Find(".markdown-table");
        Assert.Equal("region", region.GetAttribute("role"));
        Assert.Equal("Tabelle", region.GetAttribute("aria-label"));
        Assert.Equal("0", region.GetAttribute("tabindex"));
        Assert.Equal(["Stoff", "pH-Wert"], page.FindAll("thead th[scope=col]").Select(cell => cell.TextContent));
        Assert.Equal("align-right", page.Find("thead th:last-child").GetAttribute("class"));
        Assert.Equal(["Zitronensaft", "2", "Wasser", "7"], page.FindAll("tbody td").Select(cell => cell.TextContent));
        Assert.Equal("2", page.Find("tbody td strong").TextContent);
    }

    // AC2
    [Fact]
    public void Emphasis_ShowsAsEmphasis()
    {
        var page = Render("*kursiv* _auch_ **fett** __auch__ ~~weg~~ `code`");

        Assert.Equal(["kursiv", "auch"], page.FindAll("em").Select(element => element.TextContent));
        Assert.Equal(["fett", "auch"], page.FindAll("strong").Select(element => element.TextContent));
        Assert.Equal("weg", page.Find("del").TextContent);
        Assert.Equal("code", page.Find("code").TextContent);
    }

    [Fact]
    public void CodeBlock_ShowsAsText()
    {
        var page = Render("```html\n<b>fett</b>\n```\n\n> Zitat\n\n---");

        Assert.Equal("<b>fett</b>", page.Find("pre > code").TextContent);
        Assert.Empty(page.FindAll("b"));
        Assert.Equal("Zitat", page.Find("blockquote > p").TextContent);
        Assert.Single(page.FindAll("hr"));
    }

    [Fact]
    public void HardBreak_BreaksTheLine()
    {
        var page = Render("Zeile eins  \nZeile zwei\nweiter");

        Assert.Single(page.FindAll("p > br"));
        Assert.Equal("Zeile einsZeile zwei\nweiter", page.Find("p").TextContent);
    }

    // A legend or a label takes no blocks and no links: there only emphasis and code remain, the rest is text.
    [Fact]
    public void Inline_KeepsOnlyEmphasisAndCode()
    {
        var page = Render("# Titel\n\n- **eins**\n- [zwei](https://example.org)\n\n`drei`", inline: true);

        Assert.Equal(["STRONG", "CODE"], page.Nodes.OfType<IElement>().SelectMany(e => e.DescendantsAndSelf<IElement>()).Select(e => e.TagName));
        Assert.Equal("Titel eins zwei drei", TextOf(page));
    }

    // The allowlist: a node type the renderer doesn't know shows as its source. Here the parser runs with HTML on, so
    // the tree holds HTML nodes, which are on no list.
    [Fact]
    public void UnknownNode_ShowsAsItsSource()
    {
        const string text = "Text <b onclick=\"alert(1)\">fett</b>\n\n<div onmouseover=\"alert(2)\">Block</div>\n\nEnde[^1]\n\n[^1]: Fußnote";
        var pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().UseFootnotes().Build();
        var document = Markdig.Markdown.Parse(text, pipeline);
        using var context = new BunitContext();

        var page = context.Render(SafeMarkdown.Render(document, text));

        Assert.Empty(page.FindAll("b, div[onmouseover], [onclick], sup, section"));
        var paragraphs = page.FindAll("p").Select(p => p.TextContent).ToList();
        Assert.Equal("Text <b onclick=\"alert(1)\">fett</b>", paragraphs[0]);
        Assert.Equal("<div onmouseover=\"alert(2)\">Block</div>", paragraphs[1]);
        Assert.Contains("[^1]", paragraphs[2]);
        Assert.Contains("[^1]: Fußnote", paragraphs[^1]);
    }

    // Deep nesting turns into text at some depth instead of an endless tree.
    [Theory]
    [InlineData(30)]
    [InlineData(500)]
    public void DeepNesting_EndsAsText(int depth)
    {
        var text = string.Concat(Enumerable.Repeat("> ", depth)) + "unten";

        var page = Render(text);

        Assert.True(page.FindAll("blockquote").Count <= 13);
        Assert.Contains("unten", page.Find(".markdown").TextContent);
    }

    [Fact]
    public void Nothing_RendersEmpty()
    {
        Assert.Empty(Render("").Find(".markdown").Children);
        Assert.Equal("", Render("", inline: true).Markup);
    }

    private static IRenderedComponent<SafeMarkdown> Render(string text, bool inline = false, int headingOffset = 0)
    {
        var context = new BunitContext();
        return context.Render<SafeMarkdown>(parameters => parameters
            .Add(p => p.Text, text)
            .Add(p => p.Inline, inline)
            .Add(p => p.HeadingOffset, headingOffset));
    }

    private static string TextOf(IRenderedComponent<SafeMarkdown> page) =>
        string.Concat(page.Nodes.Select(node => node.TextContent));

    private static void AssertHarmless(IRenderedComponent<SafeMarkdown> page)
    {
        var elements = page.Nodes.OfType<IElement>().SelectMany(element => element.DescendantsAndSelf<IElement>()).ToList();
        Assert.DoesNotContain(elements, element => element.Matches(HostileText.Injected));
        Assert.DoesNotContain(elements, element => element.TagName is "IMG" or "SVG" or "IFRAME" or "OBJECT" or "EMBED" or "FORM" or "B" or "DIV" && !element.ClassList.Contains("markdown") && !element.ClassList.Contains("markdown-table"));
        Assert.DoesNotContain(elements.SelectMany(element => element.Attributes),
            attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || attribute.Name is "style" or "src");
        Assert.All(page.FindAll("a"), link => Assert.Contains(Schemes, scheme => link.GetAttribute("href")!.StartsWith(scheme, StringComparison.Ordinal)));
    }
}