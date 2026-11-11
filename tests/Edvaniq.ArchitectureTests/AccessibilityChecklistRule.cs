using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// docs/accessibility.md: the checklist of the definition of done for the UI keeps its four points, and every test it
// names as a check exists under tests.
internal static partial class AccessibilityChecklistRule
{
    public const string Keyboard = "### Tastatur";
    public const string Contrast = "### Kontrast";
    public const string ScreenReader = "### Screenreader";
    public const string ReducedMotion = "### Reduzierte Bewegung";

    private static readonly string[] Points = [Keyboard, Contrast, ScreenReader, ReducedMotion];

    public static IEnumerable<string> Violations(string checklist, IReadOnlySet<string> testClasses)
    {
        var headings = checklist.Split('\n').Select(line => line.TrimEnd()).ToHashSet(StringComparer.Ordinal);
        foreach (var point in Points.Where(point => !headings.Contains(point)))
        {
            yield return $"the point \"{point}\" is missing";
        }

        foreach (var name in TestName().Matches(checklist).Select(match => match.Groups[1].Value).Distinct().Where(name => !testClasses.Contains(name)))
        {
            yield return $"names {name}, which is no test class under tests";
        }
    }

    public static IReadOnlySet<string> TestClasses(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"))
            .SelectMany(file => ClassName().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"`(\w+Tests)`")]
    private static partial Regex TestName();

    [GeneratedRegex(@"\bclass\s+(\w+)")]
    private static partial Regex ClassName();
}