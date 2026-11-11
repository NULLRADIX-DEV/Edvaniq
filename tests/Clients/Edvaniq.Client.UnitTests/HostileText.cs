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

    // Elements and attributes none of the payloads may turn into.
    public const string Injected = "script, img, svg, iframe, [onerror], [onload], a[href^='javascript:']";
}