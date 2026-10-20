namespace Edvaniq.ArchitectureTests;

internal static class Rules
{
    // A rule holds when its check finds nothing. Otherwise the message lists every violation.
    public static void AssertNone(IEnumerable<string> violations, string rule, string document = "docs/architecture.md")
    {
        var found = violations.ToList();
        Assert.True(found.Count == 0, $"{rule} in {document} is broken:\n" + string.Join("\n", found));
    }
}