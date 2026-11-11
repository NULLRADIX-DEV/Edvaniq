using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// Rules 1 to 4 of tests/ReferenceSet/README.md on a small made-up document, without the AI.
public class ReferenceScoreTests
{
    // Three sections: one per anchor.
    private const string Text = "# Zelle\nDie Zelle hat einen Kern.\n\n# Atmung\nZucker wird zu Energie.\n\n## Glykolyse\nGlucose wird gespalten.";

    private static readonly ReferenceDocument Document = new(
        "test", "test.txt", null, "Biologie", "normal", true, null,
        [
            new ExpectedTopic("Zelle", ["Die Zelle"], ["hat einen Kern"], null),
            new ExpectedTopic("Zellatmung", ["Atmung"], ["wird zu Energie"], [new ExpectedTopic("Glykolyse", [], ["Glucose wird gespalten"], null)]),
        ],
        null);

    private static readonly IReadOnlyList<TextSpan> Sections = SectionSplitter.Split(Text);

    private static readonly Guid[] Ids = [.. Sections.Select(_ => Guid.NewGuid())];

    [Fact]
    public void ThreeSections_OnePerAnchor() => Assert.Equal(3, Sections.Count);

    [Fact]
    public void AnalysisAsExpected_HitsEverything()
    {
        var score = Score(new("Zelle", null, [Ids[0]]), new("Atmung", null, [Ids[1]]), new("Glykolyse", 1, [Ids[2]]));

        Assert.Equal(new ReferenceScore(3, 3, 0, 0, 3, 3), score);
    }

    // Spelling and aliases count, as Knowledge merges them.
    [Fact]
    public void NormalizedNameOrAlias_IsAHit() =>
        Assert.Equal(2, Score(new("die zelle:", null, [Ids[0]]), new("ZELLATMUNG", null, [Ids[1], Ids[2]])).Hits);

    [Fact]
    public void SecondTopicOfTheSameName_IsInvented() =>
        Assert.Equal((1, 1), Pair(Score(new("Zelle", null, [Ids[0]]), new("Zelle", null, [Ids[1], Ids[2]]))));

    [Fact]
    public void OtherLevel_IsAHitWithALevelError()
    {
        var score = Score(new("Zelle", null, [Ids[0]]), new("Atmung", null, [Ids[1]]), new("Glykolyse", null, [Ids[2]]));

        Assert.Equal((3, 1), (score.Hits, score.LevelErrors));
    }

    // The anchor of a topic may lie in a section of its subtopic.
    [Fact]
    public void AnchorInASubtopic_IsMet() =>
        Assert.Equal(3, Score(new("Zelle", null, [Ids[0]]), new("Atmung", null, []), new("Glykolyse", 1, [Ids[1], Ids[2]])).AnchorsMet);

    [Fact]
    public void AnchorWithAnotherTopic_OrOfATopicNotHit_IsNotMet()
    {
        var wrongPlace = Score(new("Zelle", null, [Ids[0], Ids[1]]), new("Atmung", null, []), new("Glykolyse", 1, [Ids[2]]));
        var notHit = Score(new("Kern", null, [Ids[0]]), new("Atmung", null, [Ids[1]]), new("Glykolyse", 1, [Ids[2]]));

        Assert.Equal(2, wrongPlace.AnchorsMet);
        Assert.Equal((2, 1, 2), (notHit.Hits, notHit.Invented, notHit.AnchorsMet));
    }

    private static ReferenceScore Score(params TopicSuggestion[] suggestions) =>
        ReferenceScore.Of(Document, Text, Sections, Ids, suggestions);

    private static (int Hits, int Invented) Pair(ReferenceScore score) => (score.Hits, score.Invented);
}