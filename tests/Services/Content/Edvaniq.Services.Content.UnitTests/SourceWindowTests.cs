using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #350: a section is shown with one neighbour on either side, never with the whole document.
public sealed class SourceWindowTests
{
    private const string First = "# Fotosynthese\nLicht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff.";
    private const string Second = "# Zellatmung\nZucker wird in den Mitochondrien zu Energie für die Zelle.";
    private const string Third = "# Gärung\nOhne Sauerstoff bauen Hefen Zucker zu Alkohol ab.";
    private const string Fourth = "# Enzyme\nEnzyme beschleunigen Reaktionen, ohne sich dabei zu verbrauchen.";

    [Fact]
    public void MiddleSection_ComesWithBothNeighbours()
    {
        var (text, sections) = Split(First, Second, Third, Fourth);

        var window = SourceWindow.Around(text, sections[2], sections);

        Assert.Equal(Join(Second, Third, Fourth), window.Text);
        Assert.Equal(Third, window.Text.Substring(window.Start, window.Length));
        Assert.True(window.TruncatedBefore);
        Assert.False(window.TruncatedAfter);
    }

    [Fact]
    public void FirstSection_HasNoNeighbourBefore()
    {
        var (text, sections) = Split(First, Second, Third);

        var window = SourceWindow.Around(text, sections[0], sections);

        Assert.Equal(Join(First, Second), window.Text);
        Assert.Equal(0, window.Start);
        Assert.Equal(First, window.Text.Substring(window.Start, window.Length));
        Assert.False(window.TruncatedBefore);
        Assert.True(window.TruncatedAfter);
    }

    [Fact]
    public void LastSection_HasNoNeighbourAfter()
    {
        var (text, sections) = Split(First, Second, Third);

        var window = SourceWindow.Around(text, sections[2], sections);

        Assert.Equal(Join(Second, Third), window.Text);
        Assert.Equal(Third, window.Text.Substring(window.Start, window.Length));
        Assert.True(window.TruncatedBefore);
        Assert.False(window.TruncatedAfter);
    }

    [Fact]
    public void OnlySection_IsTheWholeWindow()
    {
        var (text, sections) = Split(First);

        var window = SourceWindow.Around(text, sections[0], sections);

        Assert.Equal(First, window.Text);
        Assert.Equal((0, First.Length, false, false), (window.Start, window.Length, window.TruncatedBefore, window.TruncatedAfter));
    }

    // Sections further away and those of other documents don't widen the window.
    [Fact]
    public void FarAndForeignSections_AreLeftOut()
    {
        var (text, sections) = Split(First, Second, Third, Fourth);
        var (_, other) = Split(First, Second);

        var window = SourceWindow.Around(text, sections[0], [.. sections, .. other]);

        Assert.Equal(Join(First, Second), window.Text);
    }

    private static (string Text, IReadOnlyList<Section> Sections) Split(params string[] parts)
    {
        var document = Document.Paste("alice", Guid.NewGuid(), null, Join(parts), DateTime.UtcNow);
        var sections = document.Split();
        Assert.Equal(parts.Length, sections.Count);
        return (document.Text, sections);
    }

    private static string Join(params string[] parts) => string.Join("\n\n", parts);
}