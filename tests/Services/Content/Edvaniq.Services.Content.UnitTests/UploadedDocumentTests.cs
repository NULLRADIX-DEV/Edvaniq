using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// A document from a file: uploaded, titled after the file, and not split before its text is there (#282).
public sealed class UploadedDocumentTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Upload_IsUploaded_WithTheFile()
    {
        var file = Guid.NewGuid();
        var goal = Guid.NewGuid();

        var document = Document.Upload("alice", goal, "Skript Bio.pdf", file, FileFormat.Pdf, Now);

        Assert.Equal(
            (DocumentStatus.Uploaded, "Skript Bio", file, FileFormat.Pdf, goal, "", true),
            (document.Status, document.Title, document.FileId, document.FileFormat, document.LearningGoalId, document.Text, document.IsFromFile));
    }

    [Fact]
    public void PastedText_IsNotFromAFile() =>
        Assert.False(Document.Paste("alice", Guid.NewGuid(), null, "Ein Text mit genug Worten.", Now).IsFromFile);

    [Theory]
    [InlineData("Skript.pdf", "Skript")]
    [InlineData("  Notizen Kapitel 3.md  ", "Notizen Kapitel 3")]
    [InlineData("archiv.2026.docx", "archiv.2026")]
    [InlineData("../../etc/passwd.txt", "passwd")]
    [InlineData("C:\\Users\\Bob\\Desktop\\Referat.docx", "Referat")]
    [InlineData("Zeile\r\nzwei\t.txt", "Zeilezwei")]
    [InlineData(".md", ".md")]
    [InlineData("", Document.UntitledFile)]
    [InlineData(null, Document.UntitledFile)]
    [InlineData("\u0001\u0002.txt", Document.UntitledFile)]
    [InlineData("folder/", Document.UntitledFile)]
    public void Title_IsTheCleanFileName(string? fileName, string title) =>
        Assert.Equal(title, Document.TitleOf(fileName));

    [Fact]
    public void LongFileName_IsCutToATitle() =>
        Assert.Equal(Document.TitleMaxLength, Document.TitleOf(new string('a', 300) + ".pdf").Length);

    [Fact]
    public void FileDocument_IsNotSplit_BeforeItsText()
    {
        var document = Upload();

        Assert.True(document.WaitsForText);
        Assert.Throws<InvalidOperationException>(document.Split);
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
    }

    // The pages are joined by a blank line, and each knows where it lies in the text, an empty one too.
    [Fact]
    public void TakeText_JoinsThePages_AndKnowsWhereEachLies()
    {
        var document = Upload();

        var pages = document.TakeText(["  Erste Seite.\n", "", "Dritte Seite mit mehr Text."], Now);

        Assert.Equal("Erste Seite.\n\n\n\nDritte Seite mit mehr Text.", document.Text);
        Assert.Equal([(1, 0, 12), (2, 14, 0), (3, 16, 27)], pages.Select(page => (page.Number, page.Start, page.Length)));
        Assert.All(pages, page => Assert.Equal((document.Id, "alice"), (page.DocumentId, page.OwnerId)));
        Assert.Equal(["Erste Seite.", "", "Dritte Seite mit mehr Text."], pages.Select(page => document.Text.Substring(page.Start, page.Length)));
        Assert.Equal((DocumentStatus.Uploaded, (DateTime?)Now, false), (document.Status, document.TextExtractedAt, document.WaitsForText));
    }

    // From here the document is split like a pasted text, each page ends a section.
    [Fact]
    public void FileDocument_WithItsText_IsSplit()
    {
        var document = Upload();
        document.TakeText(["# Fotosynthese\nLicht spaltet Wasser.", "Zucker entsteht im Calvin-Zyklus."], Now);

        var sections = document.Split();

        Assert.Equal(DocumentStatus.Analyzing, document.Status);
        Assert.NotEmpty(sections);
    }

    // Too much text fails the document without pages. A new attempt reads the file again.
    [Fact]
    public void TakeText_OfTooMuchText_FailsTheDocument()
    {
        var document = Upload();

        var pages = document.TakeText([new string('a', Document.TextMaxLength / 2), new string('b', Document.TextMaxLength / 2)], Now);

        Assert.Empty(pages);
        Assert.Equal((DocumentStatus.Failed, Document.FileTextTooLong, (DateTime?)null), (document.Status, document.FailureReason, document.TextExtractedAt));
        Assert.Equal("", document.Text);
    }

    [Fact]
    public void TakeText_Twice_IsRefused()
    {
        var document = Upload();
        document.TakeText(["Licht spaltet Wasser."], Now);

        Assert.Throws<InvalidOperationException>(() => document.TakeText(["Noch einmal."], Now));
    }

    private static Document Upload() => Document.Upload("alice", Guid.NewGuid(), "Skript.pdf", Guid.NewGuid(), FileFormat.Pdf, Now);
}