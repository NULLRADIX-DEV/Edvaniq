using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// docs/spec/acceptance.md gives each core point of docs/spec/non-goals.md two or three criteria, each with a test case,
// under a heading named exactly like the core point.
internal static partial class AcceptanceRule
{
    // Core points with the milestones their scope sentence names, like "(M2, M5)". The points under "Nur im MVP-Plus"
    // aren't part of the MVP core and get no criteria.
    public static IReadOnlyList<(string Name, string[] Milestones)> CorePoints(string nonGoals)
    {
        var lines = nonGoals.Split('\n').Select(line => line.Trim()).ToList();
        var plusOnly = lines.IndexOf("## Nur im MVP-Plus");
        return lines.Take(plusOnly < 0 ? lines.Count : plusOnly)
            .Select((line, index) => (line, index))
            .Where(heading => heading.line.StartsWith("### "))
            .Select(heading => (heading.line["### ".Length..], MilestonesOf(lines.Skip(heading.index + 1).FirstOrDefault(line => line.Length > 0) ?? "")))
            .ToList();
    }

    // The table rows under each "### " heading, in the order of the page.
    public static IReadOnlyList<(string Name, List<(string Id, string TestCase, string From)> Rows)> Criteria(string acceptance)
    {
        var sections = new List<(string Name, List<(string Id, string TestCase, string From)> Rows)>();
        List<(string Id, string TestCase, string From)>? current = null;
        foreach (var line in acceptance.Split('\n').Select(line => line.Trim()))
        {
            if (line.StartsWith("### "))
            {
                current = [];
                sections.Add((line["### ".Length..], current));
            }
            else if (line.StartsWith("## "))
            {
                // a group like "## Ergänzungen" ends the section
                current = null;
            }
            else if (current is not null && line.StartsWith('|') && !Separator().IsMatch(line))
            {
                // cells[0] is the empty text before the first '|', a short row leaves the missing cells empty
                var cells = line.Split('|', StringSplitOptions.TrimEntries);
                if (cells.ElementAtOrDefault(1) != "ID")
                {
                    current.Add((cells.ElementAtOrDefault(1) ?? "", cells.ElementAtOrDefault(3) ?? "", cells.ElementAtOrDefault(5) ?? ""));
                }
            }
        }

        return sections;
    }

    // Every core point needs a section, and every section belongs to a core point. A section has two or three criteria,
    // each with an ID of its own, a test case in the form Gegeben, Wenn, Dann and a milestone the core point is built in.
    public static IEnumerable<string> Violations(
        IReadOnlyList<(string Name, string[] Milestones)> corePoints,
        IReadOnlyList<(string Name, List<(string Id, string TestCase, string From)> Rows)> criteria)
    {
        var milestones = corePoints.GroupBy(point => point.Name).ToDictionary(name => name.Key, name => name.First().Milestones);
        var sections = criteria.Select(section => section.Name).ToHashSet();
        var pointTwice = corePoints
            .GroupBy(point => point.Name)
            .Where(name => name.Count() > 1)
            .Select(name => $"{name.Key} is {name.Count()} times in docs/spec/non-goals.md");
        var noMilestone = corePoints
            .Where(point => point.Milestones.Length == 0)
            .Select(point => $"{point.Name} names no milestone in docs/spec/non-goals.md");
        var missing = corePoints
            .Where(point => !sections.Contains(point.Name))
            .Select(point => $"{point.Name} has no criteria");
        var unknown = criteria
            .Where(section => !milestones.ContainsKey(section.Name))
            .Select(section => $"{section.Name} is no core point in docs/spec/non-goals.md");
        var twice = criteria
            .GroupBy(section => section.Name)
            .Where(name => name.Count() > 1)
            .Select(name => $"{name.Key} has {name.Count()} sections");
        var count = criteria
            .Where(section => section.Rows.Count is < 2 or > 3)
            .Select(section => $"{section.Name} needs two or three criteria, not {section.Rows.Count}");
        var outside = criteria
            .Where(section => milestones.ContainsKey(section.Name))
            .SelectMany(section => section.Rows
                .Where(row => MilestoneId().IsMatch(row.From) && !milestones[section.Name].Contains(row.From))
                .Select(row => $"{row.Id} holds from {row.From}, but {section.Name} is built in {string.Join(", ", milestones[section.Name])}"));
        var rows = criteria.SelectMany(section => section.Rows).ToList();
        var badRows = rows.SelectMany(row => RowViolations(row.Id, row.TestCase, row.From));
        var duplicate = rows
            .GroupBy(row => row.Id)
            .Where(id => id.Count() > 1)
            .Select(id => $"{id.Key} is used {id.Count()} times");
        return pointTwice.Concat(noMilestone).Concat(missing).Concat(unknown).Concat(twice).Concat(count).Concat(outside)
            .Concat(badRows).Concat(duplicate);
    }

    private static IEnumerable<string> RowViolations(string id, string testCase, string from)
    {
        if (!Id().IsMatch(id))
        {
            yield return $"'{id}' is no ID like A1";
        }

        if (!(testCase.StartsWith("Gegeben ") && testCase.Contains("Wenn ") && testCase.Contains("dann ")))
        {
            yield return $"{id} has no test case in the form Gegeben, Wenn, Dann";
        }

        if (!MilestoneId().IsMatch(from))
        {
            yield return $"{id} holds from '{from}', which is no milestone like M2";
        }
    }

    private static string[] MilestonesOf(string scope) =>
        Milestones().Matches(scope).SelectMany(match => match.Groups[1].Value.Split(", ")).Distinct().ToArray();

    // "(M2, M5)" but not "(MVP24)", which the scope sentence of a point may name as well.
    [GeneratedRegex(@"\((M\d(?:, M\d)*)\)")]
    private static partial Regex Milestones();

    [GeneratedRegex(@"^M\d$")]
    private static partial Regex MilestoneId();

    [GeneratedRegex(@"^A\d+$")]
    private static partial Regex Id();

    // "|---|---|" and its variants with spaces or colons
    [GeneratedRegex(@"^\|[\s:|-]+$")]
    private static partial Regex Separator();
}