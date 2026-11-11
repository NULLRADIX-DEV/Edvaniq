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
    public void FileDocument_IsNotSplit()
    {
        var document = Document.Upload("alice", Guid.NewGuid(), "Skript.pdf", Guid.NewGuid(), FileFormat.Pdf, Now);

        Assert.Throws<InvalidOperationException>(document.Split);
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
    }
}