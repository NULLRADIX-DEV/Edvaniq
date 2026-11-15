using System.Text.RegularExpressions;

using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #410: the demo material fits a pasted text, gives enough topics and has what the demo shows: definitions, code and
// formulas, each topic in its own section.
public partial class DemoMaterialTests
{
    private static IReadOnlyList<DemoDocument> Documents => DemoMaterial.Documents;

    [Fact]
    public void Material_IsThreeDocuments_InTheOrderOfTheDemo() =>
        Assert.Equal(["Suchen in Listen", "Sortierverfahren", "Komplexität von Algorithmen"], Documents.Select(document => document.Title));

    [Fact]
    public void EveryDocument_PassesAsPastedText_WithItsTitle() =>
        Assert.All(Documents, document =>
            Assert.Equal(((string?)null, (string?)null), (Document.CheckText(document.Text), Document.CheckTitle(document.Title))));

    // Each "## " heading is a topic of its document's "# " heading, the simulated AI and a real one follow them.
    [Fact]
    public void Material_HasAtLeastSixTopics_EachInASectionOfItsOwn()
    {
        var topics = Documents.SelectMany(document => Lines(document).Where(line => line.StartsWith("## ", StringComparison.Ordinal))).ToList();

        Assert.True(topics.Count >= 6, $"{topics.Count} topics");
        Assert.Equal(topics.Count, topics.Distinct().Count());
        Assert.All(Documents, document =>
        {
            var headings = SectionSplitter.Split(document.Text)
                .Select(section => SectionSplitter.HeadingOf(document.Text, section.Start))
                .Distinct()
                .Count();
            Assert.Equal(Lines(document).Count(line => line.StartsWith('#')), headings);
        });
    }

    [Fact]
    public void EveryDocument_HasDefinitionsAndAFormula()
    {
        Assert.All(Documents, document =>
        {
            Assert.Contains("Definition:", document.Text, StringComparison.Ordinal);
            Assert.Matches(Formula(), document.Text);
        });
    }

    // A line starting with '#' inside the code would be taken for a heading.
    [Fact]
    public void Material_HasPythonCode_WithoutLinesTakenForHeadings()
    {
        var code = Documents.SelectMany(document => CodeLines(document)).ToList();

        Assert.NotEmpty(code);
        Assert.Contains(Documents, document => document.Text.Contains("```python\ndef ", StringComparison.Ordinal));
        Assert.DoesNotContain(code, line => line.TrimStart().StartsWith('#'));
    }

    private static string[] Lines(DemoDocument document) => document.Text.Split('\n');

    private static IEnumerable<string> CodeLines(DemoDocument document)
    {
        var inCode = false;
        foreach (var line in Lines(document))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
            }
            else if (inCode)
            {
                yield return line;
            }
        }
    }

    // A function of n as the formulas read once pasted: log2(n), T(n), O(n²) and the like.
    [GeneratedRegex(@"\b(log2|T|O)\([^)]*n[^)]*\)")]
    private static partial Regex Formula();
}