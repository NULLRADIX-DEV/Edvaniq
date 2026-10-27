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
    public void PasteWithoutTitle_CutsALongFirstLine()
    {
        var line = new string('a', Document.TitleMaxLength - 1) + " und noch viel mehr";

        Assert.Equal(new string('a', Document.TitleMaxLength - 1), Document.Paste("alice", null, line, Now).Title);
    }
}
