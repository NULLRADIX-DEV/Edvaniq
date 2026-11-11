using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// The reference set stays usable: every document fits the limits of a pasted text, has the topics we expect from it,
// and the rule in tests/ReferenceSet/README.md can be applied to it without asking the AI.
public class ReferenceSetTests
{
    private static readonly string[] Kinds = ["normal", "scan", "injection"];

    private static ReferenceSet Set => ReferenceSet.Load();

    [Fact]
    public void Set_HasAtLeastFiveDocuments_WithAScanAndAnInjection()
    {
        Assert.True(Set.Version >= 1);
        Assert.True(Set.Documents.Count >= 5, $"The reference set has {Set.Documents.Count} documents, at least 5 are needed.");
        Assert.Contains(Set.Documents, document => document.Kind == "scan");
        Assert.Contains(Set.Documents, document => document.Kind == "injection");
    }

    [Fact]
    public void Manifest_AndFolder_NameTheSameFiles()
    {
        var listed = Set.Documents.Select(document => document.File).Order(StringComparer.Ordinal);
        var present = Directory.GetFiles(ReferenceSet.Folder, "*.txt").Select(Path.GetFileName).Order(StringComparer.Ordinal);

        Assert.Equal(listed, present);
        Assert.Equal(Set.Documents.Count, Set.Documents.Select(document => document.Id).Distinct().Count());
    }

    [Fact]
    public void EveryDocument_FitsThePasteLimits() =>
        AssertNoViolations(document =>
        {
            var violations = new List<string>();
            if (!Kinds.Contains(document.Kind))
            {
                violations.Add($"unknown kind '{document.Kind}'");
            }

            if (document.Text.Length > Document.TextMaxLength)
            {
                violations.Add($"text has {document.Text.Length} characters, more than {Document.TextMaxLength}");
            }

            if (string.IsNullOrWhiteSpace(document.Text))
            {
                violations.Add("text is blank, a pasted text never is");
            }

            if (document.Title is { Length: > Document.TitleMaxLength })
            {
                violations.Add($"title is longer than {Document.TitleMaxLength}");
            }

            return violations;
        });

    [Fact]
    public void AnalyzableDocuments_ExpectTopicsTheAnalysisCanGive() =>
        AssertNoViolations(document =>
        {
            var violations = new List<string>();
            var topics = document.AllTopics().ToList();
            if (document.Analyzable != (document.Kind != "scan"))
            {
                violations.Add("only the scan is not analyzable");
            }

            if (document.Analyzable == (topics.Count == 0))
            {
                violations.Add(document.Analyzable ? "expects no topics" : "expects topics although it is not analyzable");
            }

            if (topics.Count > TopicAnalysis.MaxTopics)
            {
                violations.Add($"expects {topics.Count} topics, the analysis keeps at most {TopicAnalysis.MaxTopics}");
            }

            foreach (var (topic, path) in topics)
            {
                if (topic.Names.Any(name => name.Length > TopicAnalysis.NameMaxLength || ReferenceSet.Normalize(name).Length == 0))
                {
                    violations.Add($"'{path}' has a name or alias that is empty or longer than {TopicAnalysis.NameMaxLength}");
                }

                if (path.Contains(" / ", StringComparison.Ordinal) && topic.Subtopics is { Count: > 0 })
                {
                    violations.Add($"'{path}' goes deeper than two levels");
                }

                if (topic.Subtopics is not { Count: > 0 } && topic.Anchors is not { Count: > 0 })
                {
                    violations.Add($"'{path}' has neither subtopics nor anchors");
                }
            }

            violations.AddRange(topics
                .SelectMany(entry => entry.Topic.Names.Select(name => (Name: ReferenceSet.Normalize(name), entry.Path)))
                .GroupBy(entry => entry.Name)
                .Where(group => group.Count() > 1)
                .Select(group => $"the name '{group.Key}' stands for more than one topic: {string.Join(", ", group.Select(entry => entry.Path))}"));
            return violations;
        });

    [Fact]
    public void EveryAnchor_PointsToOneSection_OfItsTopicAlone() =>
        AssertNoViolations(document =>
        {
            var violations = new List<string>();
            var text = document.Text;
            var sections = SectionSplitter.Split(text);
            var placed = new List<(string Path, int Section)>();
            foreach (var (topic, path) in document.AllTopics())
            {
                foreach (var anchor in topic.Anchors ?? [])
                {
                    var at = text.IndexOf(anchor, StringComparison.Ordinal);
                    if (at < 0 || text.IndexOf(anchor, at + 1, StringComparison.Ordinal) >= 0)
                    {
                        violations.Add($"anchor '{anchor}' of '{path}' is {(at < 0 ? "not in the text" : "in the text more than once")}");
                        continue;
                    }

                    var section = sections.ToList().FindIndex(span => span.Start <= at && at + anchor.Length <= span.End);
                    if (section < 0)
                    {
                        violations.Add($"anchor '{anchor}' of '{path}' crosses two sections");
                        continue;
                    }

                    placed.Add((path, section));
                }
            }

            // A topic and its own subtopic may share a section, the section then belongs to the subtopic.
            violations.AddRange(placed
                .SelectMany(one => placed
                    .Where(other => string.CompareOrdinal(one.Path, other.Path) < 0
                        && one.Section == other.Section
                        && !Related(one.Path, other.Path))
                    .Select(other => $"section {one.Section + 1} holds anchors of '{one.Path}' and '{other.Path}'"))
                .Distinct());
            return violations;
        });

    [Fact]
    public void Injection_CarriesTheTextsItMustNotLeadTo() =>
        AssertNoViolations(document =>
        {
            var forbidden = document.Forbidden ?? [];
            if (document.Kind != "injection")
            {
                return forbidden.Count > 0 ? ["only an injection has forbidden texts"] : [];
            }

            if (forbidden.Count == 0)
            {
                return ["the injection names no forbidden texts"];
            }

            var violations = forbidden
                .Where(text => !document.Text.Contains(text, StringComparison.Ordinal))
                .Select(text => $"forbidden text '{text}' is not in the document")
                .ToList();
            violations.AddRange(document.AllTopics()
                .Where(entry => entry.Topic.Names.Any(name => forbidden.Any(text => name.Contains(text, StringComparison.OrdinalIgnoreCase))))
                .Select(entry => $"'{entry.Path}' contains a forbidden text"));
            return violations;
        });

    [Fact]
    public void Normalize_FoldsSpellingButKeepsWords()
    {
        Assert.Equal("ph wert", ReferenceSet.Normalize("  pH-Wert "));
        Assert.Equal("einflussfaktoren der fotosynthese", ReferenceSet.Normalize("Einflussfaktoren der Fotosynthese:"));
        Assert.Equal("saeuren und basen nach brønsted", ReferenceSet.Normalize("Säuren und Basen nach Brønsted"));
        Assert.Equal("grossschreibung", ReferenceSet.Normalize("Großschreibung"));
        Assert.NotEqual(ReferenceSet.Normalize("Bubble Sort"), ReferenceSet.Normalize("Bubblesort"));
    }

    private static bool Related(string one, string other) =>
        one.StartsWith(other + " / ", StringComparison.Ordinal) || other.StartsWith(one + " / ", StringComparison.Ordinal);

    private static void AssertNoViolations(Func<ReferenceDocument, IEnumerable<string>> check)
    {
        var violations = Set.Documents
            .SelectMany(document => check(document).Select(violation => $"{document.Id}: {violation}"))
            .ToList();
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}