namespace Edvaniq.ArchitectureTests;

// docs/licenses.md as the license check reads it: the SPDX ids in the paragraph "Erlaubt" and the rows of the tables
// under "## Liste", each with the "### " heading above it as its table. A row names its component in the first
// backticked word of its first cell, or in the whole cell if it has none, then its license and its use. A row whose
// license is not allowed counts as approved if its use starts with "Einzelfreigabe:", "Vorläufige Ausnahme:" or
// "Server-Software:".
internal sealed record LicenseList(IReadOnlySet<string> Allowed, IReadOnlyList<LicenseList.Row> Rows)
{
    public const string Packages = "NuGet-Pakete";
    public const string IndirectPackages = "Indirekte NuGet-Pakete";
    public const string Tools = "Tools und Programme";
    public const string Actions = "GitHub Actions";
    public const string Images = "Container-Images";

    private static readonly string[] Approvals = ["Einzelfreigabe:", "Vorläufige Ausnahme:", "Server-Software:"];

    public sealed record Row(string Table, string Component, string License, string Use)
    {
        public bool IsApproved => Approvals.Any(approval => Use.StartsWith(approval, StringComparison.Ordinal));
    }

    public static LicenseList Read(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.Trim()).ToList();
        var allowed = lines.Where(line => line.StartsWith("**Erlaubt**", StringComparison.Ordinal))
            .SelectMany(Backticked)
            .ToHashSet(StringComparer.Ordinal);

        var rows = new List<Row>();
        string? table = null;
        foreach (var line in lines.SkipWhile(line => line != "## Liste").Skip(1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                table = line[4..];
            }
            else if (table is not null && line.StartsWith('|') && !line.StartsWith("| Komponente |", StringComparison.Ordinal) && !line.StartsWith("|---", StringComparison.Ordinal))
            {
                var cells = line.Split('|', StringSplitOptions.TrimEntries);
                var component = Backticked(cells[1]).FirstOrDefault() ?? cells[1];
                rows.Add(new Row(table, component, cells[2], cells[3]));
            }
        }

        return new LicenseList(allowed, rows);
    }

    // An SPDX expression is allowed if each part joined by AND is, or at least one part joined by OR. A WITH exception
    // or a license that is no SPDX id is never allowed.
    public bool IsAllowed(string expression)
    {
        var trimmed = expression.Trim();
        if (Split(trimmed, " OR ") is { Count: > 1 } any)
        {
            return any.Any(IsAllowed);
        }

        if (Split(trimmed, " AND ") is { Count: > 1 } all)
        {
            return all.All(IsAllowed);
        }

        return trimmed.StartsWith('(') && trimmed.EndsWith(')') ? IsAllowed(trimmed[1..^1]) : Allowed.Contains(trimmed);
    }

    // The parts of the expression between the operator, outside of parentheses.
    private static List<string> Split(string expression, string separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < expression.Length; index++)
        {
            depth += expression[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0 && string.CompareOrdinal(expression, index, separator, 0, separator.Length) == 0)
            {
                parts.Add(expression[start..index]);
                start = index + separator.Length;
            }
        }

        parts.Add(expression[start..]);
        return parts;
    }

    private static IEnumerable<string> Backticked(string text) =>
        text.Split('`').Where((_, index) => index % 2 == 1);
}