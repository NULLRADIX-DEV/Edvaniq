using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// A pasted text becomes a document only with content and within the limits, and the client shows the reason otherwise.
public class DocumentTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    public void EmptyText_IsRejected(string? text) =>
        Assert.Equal("Der Text ist leer. Füge den Text ein, aus dem du lernen willst.", Document.CheckText(text));

    [Fact]
    public void TextAtTheLimit_IsAccepted() =>
        Assert.Null(Document.CheckText(new string('a', Document.TextMaxLength)));

    [Fact]
    public void TextOverTheLimit_IsRejected() =>
        Assert.Equal(
            "Der Text ist zu lang. Erlaubt sind höchstens 200.000 Zeichen.",
            Document.CheckText(new string('a', Document.TextMaxLength + 1)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Photosynthese")]
    public void MissingOrShortTitle_IsAccepted(string? title) =>
        Assert.Null(Document.CheckTitle(title));

    [Fact]
    public void TitleAtTheLimit_CountsWithoutTheSpacesAround() =>
        Assert.Null(Document.CheckTitle($"  {new string('a', Document.TitleMaxLength)}  "));

    [Fact]
    public void TitleOverTheLimit_IsRejected() =>
        Assert.Equal(
            "Der Titel ist zu lang. Erlaubt sind höchstens 80 Zeichen.",
            Document.CheckTitle(new string('a', Document.TitleMaxLength + 1)));

    [Fact]
    public void Paste_KeepsTheTextAsItIs_AndStartsAsUploaded()
    {
        const string text = "\r\n  Photosynthese \r\nLicht wird zu Zucker.  ";

        var document = Document.Paste("alice", "  Biologie  ", text, Now);

        Assert.Equal("alice", document.OwnerId);
        Assert.Equal("Biologie", document.Title);
        Assert.Equal(text, document.Text);
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
        Assert.Equal(Now, document.CreatedAt);
        Assert.NotEqual(Guid.Empty, document.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void PasteWithoutTitle_TakesTheFirstLineWithContent(string? title) =>
        Assert.Equal("Photosynthese", Document.Paste("alice", title, "\r\n  \r\n  Photosynthese \r\nLicht wird zu Zucker.", Now).Title);

    [Fact]
    public void Split_GivesSectionsOfTheDocument_AndStartsTheAnalysis()
    {
        var document = Document.Paste("alice", null, "# Photosynthese\nLicht spaltet Wasser.\n\n# Zellatmung\nZucker wird zu Energie.", Now);

        var sections = document.Split();

        Assert.Equal(DocumentStatus.Analyzing, document.Status);
        Assert.Null(document.FailureReason);
        Assert.Equal([(0, 0, 37), (1, 39, 36)], sections.Select(section => (section.Ordinal, section.Start, section.Length)));
        Assert.All(sections, section => Assert.Equal((document.Id, "alice"), (section.DocumentId, section.OwnerId)));
    }

    [Fact]
    public void Split_OfWhiteSpace_FailsTheDocumentWithAReason()
    {
        var document = Document.Paste("alice", "Leer", " \r\n ", Now);

        Assert.Empty(document.Split());
        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("Der Text enthält nichts, woraus sich lernen lässt. Füge einen anderen Text ein.", document.FailureReason);
    }

    [Fact]
    public void Split_Twice_IsRefused()
    {
        var document = Document.Paste("alice", null, "Licht spaltet Wasser.", Now);
        document.Split();

        Assert.Throws<InvalidOperationException>(() => document.Split());
    }

    [Fact]
    public void FinishAnalysis_MakesTheDocumentReady()
    {
        var document = Document.Paste("alice", null, "Licht spaltet Wasser.", Now);
        document.Split();

        document.FinishAnalysis();

        Assert.Equal(DocumentStatus.Ready, document.Status);
    }

    [Fact]
    public void FinishAnalysis_BeforeTheSplit_IsRefused() =>
        Assert.Throws<InvalidOperationException>(() => Document.Paste("alice", null, "Licht spaltet Wasser.", Now).FinishAnalysis());

    [Fact]
    public void Fail_CutsALongReason()
    {
        var document = Document.Paste("alice", null, "Licht spaltet Wasser.", Now);

        document.Fail(new string('x', Document.FailureReasonMaxLength + 1));

        Assert.Equal(Document.FailureReasonMaxLength, document.FailureReason?.Length);
    }

    [Fact]
    public void PasteWithoutTitle_CutsALongFirstLine()
    {
        var line = new string('a', Document.TitleMaxLength - 1) + " und noch viel mehr";

        Assert.Equal(new string('a', Document.TitleMaxLength - 1), Document.Paste("alice", null, line, Now).Title);
    }
}
