using System.Text;

using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// A text falls into sections of a useful size along its paragraphs and headings, and no word gets lost on the way.
public class SectionSplitterTests
{
    [Fact]
    public void ShortParagraphs_ShareASection()
    {
        const string text = "Licht spaltet Wasser.\r\n\r\nDabei entsteht Sauerstoff.\n\nCO2 wird zu Zucker.\n";

        Assert.Equal(["Licht spaltet Wasser.\r\n\r\nDabei entsteht Sauerstoff.\n\nCO2 wird zu Zucker."], Split(text));
    }

    [Fact]
    public void Heading_StartsASection_AndKeepsTheParagraphBelow()
    {
        const string text = """
            # Photosynthese
            ## Lichtreaktion
            Licht spaltet Wasser.
            ## Calvin-Zyklus
            CO2 wird zu Zucker.

            # Zellatmung
            Zucker wird zu Energie.
            """;

        Assert.Equal(
            [
                "# Photosynthese\n## Lichtreaktion\nLicht spaltet Wasser.",
                "## Calvin-Zyklus\nCO2 wird zu Zucker.",
                "# Zellatmung\nZucker wird zu Energie.",
            ],
            Split(text.ReplaceLineEndings("\n")));
    }

    [Fact]
    public void HashWithoutSpace_IsNoHeading() =>
        Assert.Single(Split("Licht spaltet Wasser.\n#Photosynthese ist ein Hashtag."));

    [Fact]
    public void Paragraphs_StopSharingAtTheTargetLength()
    {
        var paragraph = Sentences(SectionSplitter.TargetLength * 2 / 3);
        var text = $"{paragraph}\n\n{paragraph}\n\n{paragraph}";

        Assert.Equal([paragraph, paragraph, paragraph], Split(text));
    }

    [Fact]
    public void LongParagraph_IsCutAtSentenceEnds()
    {
        var text = Sentences(SectionSplitter.MaxLength * 3);

        var sections = Split(text);

        // Only the last piece can be short, it holds what is left.
        Assert.True(sections.Count >= 3, $"{sections.Count} sections");
        Assert.All(sections[..^1], section => Assert.InRange(section.Length, SectionSplitter.MaxLength / 2, SectionSplitter.MaxLength));
        Assert.All(sections, section => Assert.EndsWith(".", section));
    }

    [Fact]
    public void WordLongerThanAllowed_IsCutHard()
    {
        var text = new string('a', SectionSplitter.MaxLength * 2 + 100);

        Assert.Equal(
            [SectionSplitter.MaxLength, SectionSplitter.MaxLength, 100],
            Split(text).Select(section => section.Length));
    }

    [Fact]
    public void HardCut_DoesNotSplitAnEmoji()
    {
        var text = new string('a', SectionSplitter.MaxLength - 1) + "😀" + "bbb";

        Assert.Equal([new string('a', SectionSplitter.MaxLength - 1), "😀bbb"], Split(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t\n ")]
    public void WhiteSpace_HasNoSections(string text) =>
        Assert.Empty(SectionSplitter.Split(text));

    [Fact]
    public void Sections_CoverAllContent_InOrder()
    {
        var text = $"\r\n  # Titel\r\n{Sentences(4_000)}\r\n\r\n## Teil\r\nKurz.\r\n{new string('x', 7_000)}  \r\n";

        var spans = SectionSplitter.Split(text);

        var end = 0;
        foreach (var span in spans)
        {
            Assert.True(string.IsNullOrWhiteSpace(text[end..span.Start]), $"Content lost before {span.Start}.");
            Assert.False(char.IsWhiteSpace(text[span.Start]));
            Assert.False(char.IsWhiteSpace(text[span.End - 1]));
            end = span.End;
        }

        Assert.True(string.IsNullOrWhiteSpace(text[end..]), "Content lost at the end.");
    }

    private static List<string> Split(string text) =>
        [.. SectionSplitter.Split(text).Select(span => text.Substring(span.Start, span.Length))];

    // Short sentences with a space between, at least length characters in all, ending on a full stop.
    private static string Sentences(int length)
    {
        var text = new StringBuilder();
        for (var number = 0; text.Length < length; number++)
        {
            text.Append(text.Length == 0 ? "" : " ").Append($"Satz Nummer {number:D5} ist kurz.");
        }

        return text.ToString();
    }
}
