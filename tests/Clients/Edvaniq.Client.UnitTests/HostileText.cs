namespace Edvaniq.Client.TestData;

// Text from a user or the AI that a browser would run if a page wrote it as markup. Each payload sets window.__xss or
// opens a dialog when it runs. Shared by the bUnit tests and the browser tests (linked into both projects).
internal static class HostileText
{
    public static readonly string[] Payloads =
    [
        "<script>window.__xss = 1</script>",
        "<img src=x onerror=\"window.__xss = 2\">",
        "<a href=\"javascript:window.__xss = 3\">Hier klicken</a>",
        "</textarea><script>window.__xss = 4</script>",
        "\"><svg onload=\"window.__xss = 5\">",
        "<img src=x onerror=alert(1)>",
    ];

    // The same in Markdown: links and images that run script, raw HTML as block and inline, attributes in braces and
    // nesting deep enough to hide something at the bottom.
    public static readonly string[] MarkdownPayloads =
    [
        "[Hier klicken](javascript:window.__xss=11)",
        "[Hier klicken](JaVaScRiPt:window.__xss=12)",
        "[Hier klicken](java&#115;cript:window.__xss=13)",
        "[Hier klicken](<javascript:window.__xss = 14>)",
        "[Hier klicken](data:text/html;base64,PHNjcmlwdD53aW5kb3cuX194c3MgPSAxNTwvc2NyaXB0Pg==)",
        "[Hier klicken](vbscript:window.__xss=16)",
        "<javascript:window.__xss=17>",
        "[Hier klicken][ziel]\n\n[ziel]: javascript:window.__xss=18",
        "![Bild](javascript:window.__xss=19)",
        "![Bild](https://example.org/bild.png)",
        "Vorher\n\n<script>window.__xss = 20</script>\n\nNachher",
        "<div onmouseover=\"window.__xss = 21\">\n\n**fett**\n\n</div>",
        "Text <b onclick=\"window.__xss = 22\">fett</b> und <img src=x onerror=\"window.__xss = 23\"> weiter",
        "**fett**{onclick=\"window.__xss = 24\"}",
        "| Spalte |\n|---|\n| <img src=x onerror=\"window.__xss = 25\"> |",
        "- eins\n  - zwei\n    - drei\n      - <svg onload=\"window.__xss = 26\">\n        1. [tief](javascript:window.__xss=27)",
        string.Concat(Enumerable.Repeat("> ", 200)) + "<img src=x onerror=\"window.__xss = 28\">",
        string.Concat(Enumerable.Range(0, 100).Select(level => new string(' ', level * 2) + "- x\n")) + "[tief](javascript:window.__xss=29)",
        "```html\n<script>window.__xss = 30</script>\n```",
    ];

    // Elements and attributes none of the payloads may turn into.
    public const string Injected = "script, img, svg, iframe, [onerror], [onload], a[href^='javascript:']";
}