namespace Edvaniq.ArchitectureTests;

// docs/accessibility.md: the accessibility checklist is part of the definition of done and of every review.
public class AccessibilityChecklistTests
{
    [Fact]
    public void Checklist_HasItsFourPoints_AndNamesOnlyRealTests()
    {
        var checklist = File.ReadAllText(Path.Combine(Repository.Root, "docs", "accessibility.md"));

        Rules.AssertNone(
            AccessibilityChecklistRule.Violations(checklist, AccessibilityChecklistRule.TestClasses(Repository.Root)),
            "The accessibility checklist",
            "docs/accessibility.md");
    }

    [Fact]
    public void Review_PointsToTheChecklist() =>
        Assert.Contains("(accessibility.md)", File.ReadAllText(Path.Combine(Repository.Root, "docs", "branch-protection.md")));

    [Fact]
    public void BrokenChecklist_IsReported()
    {
        const string checklist = """
            ### Tastatur

            `KeyboardTests` presses Tab.

            ### Kontrast

            `ContrastTests` and `GoneTests`.
            """;

        Assert.Equal(
            [
                "the point \"### Screenreader\" is missing",
                "the point \"### Reduzierte Bewegung\" is missing",
                "names GoneTests, which is no test class under tests",
            ],
            AccessibilityChecklistRule.Violations(checklist, new HashSet<string> { "KeyboardTests", "ContrastTests" }));
    }
}