using System.Text;

using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// Scans the page as it stands with axe-core, against WCAG 2.0, 2.1 and 2.2 level A and AA. Only violations count. What
// axe cannot decide, it reports as incomplete, and this scan leaves that to the manual test.
public static class AxeScan
{
    private const int TargetsPerRule = 10;

    private static readonly AxeRunOptions Options = new()
    {
        RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
    };

    public static async Task<IReadOnlyList<AxeResultItem>> ViolationsAsync(IPage page)
    {
        var result = await page.RunAxe(Options);
        return result.Violations;
    }

    public static async Task AssertNoViolationsAsync(IPage page, string where)
    {
        var violations = await ViolationsAsync(page);
        if (violations.Count > 0)
        {
            Assert.Fail(Describe(violations, where));
        }
    }

    // Every violation with rule, impact, the elements and the page that explains how to fix it.
    public static string Describe(IReadOnlyList<AxeResultItem> violations, string where)
    {
        var text = new StringBuilder();
        text.AppendLine($"{violations.Count} accessibility {(violations.Count == 1 ? "rule" : "rules")} violated on {where}:");
        foreach (var violation in violations)
        {
            text.AppendLine();
            text.AppendLine($"{violation.Id} ({violation.Impact}): {violation.Help}");
            text.AppendLine($"  {violation.HelpUrl}");
            foreach (var node in violation.Nodes.Take(TargetsPerRule))
            {
                text.AppendLine($"  - {node.Target}");
                text.AppendLine($"    {node.Html}");
            }

            if (violation.Nodes.Length > TargetsPerRule)
            {
                text.AppendLine($"  ... and {violation.Nodes.Length - TargetsPerRule} more");
            }
        }

        return text.ToString();
    }
}