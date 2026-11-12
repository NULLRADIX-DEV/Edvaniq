using System.IO.Compression;
using System.Text;

using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// A file is what its bytes say, and they have to agree with its extension. Every file here is made in the test.
public sealed class FileCheckTests
{
    [Theory]
    [InlineData("Skript.pdf", FileFormat.Pdf)]
    [InlineData("SKRIPT.PDF", FileFormat.Pdf)]
    [InlineData("Aufsatz.docx", FileFormat.Docx)]
    [InlineData("notizen.txt", FileFormat.Text)]
    [InlineData("README.md", FileFormat.Markdown)]
    [InlineData("liste.markdown", FileFormat.Markdown)]
    public void Extension_NamesTheFormat(string name, FileFormat format) =>
        Assert.Equal(format, FileCheck.FormatOf(name));

    [Theory]
    [InlineData("Folien.pptx")]
    [InlineData("Tabelle.xlsx")]
    [InlineData("Foto.png")]
    [InlineData("Scan.jpg")]
    [InlineData("Alt.doc")]
    [InlineData("ohne-endung")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("skript.pdf.exe")]
    public void OtherTypes_HaveNoFormat(string? name) =>
        Assert.Null(FileCheck.FormatOf(name));

    [Fact]
    public void Pdf_Matches() =>
        Assert.True(FileCheck.Matches(Pdf(), FileFormat.Pdf));

    [Fact]
    public void Docx_Matches() =>
        Assert.True(FileCheck.Matches(Zip("word/document.xml"), FileFormat.Docx));

    [Theory]
    [InlineData("Ein Text über Photosynthese.\r\nZweite Zeile\tmit Tab.")]
    [InlineData("# Überschrift\n\n- Punkt mit Umlaut äöü und Emoji 🌱\f")]
    [InlineData("")]
    public void Utf8Text_Matches(string text)
    {
        Assert.True(FileCheck.Matches(Bytes(Encoding.UTF8.GetBytes(text)), FileFormat.Text));
        Assert.True(FileCheck.Matches(Bytes(Encoding.UTF8.GetBytes(text)), FileFormat.Markdown));
    }

    [Fact]
    public void Utf8Text_WithByteOrderMark_Matches() =>
        Assert.True(FileCheck.Matches(Bytes([.. Encoding.UTF8.GetPreamble(), .. "Text"u8.ToArray()]), FileFormat.Text));

    // #318: a Word file with a password, or an old .doc renamed, is a compound file. The user learns what to do about it.
    [Fact]
    public void CompoundFileAsDocx_IsRefusedAsProtected() =>
        Assert.Equal(FileCheck.Protected, FileCheck.Refusal(Bytes([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, .. new byte[504]]), FileFormat.Docx));

    // A real PDF marks itself as binary in its second line.
    [Fact]
    public void PdfNamedAsText_IsRefusedAsMismatch() =>
        Assert.Equal(FileCheck.Mismatch, FileCheck.Refusal(Bytes([.. "%PDF-1.7\n%"u8.ToArray(), 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']), FileFormat.Text));

    [Fact]
    public void MatchingFile_IsNotRefused() =>
        Assert.Null(FileCheck.Refusal(Zip("word/document.xml"), FileFormat.Docx));

    // A text longer than one read of the checker, with the bad byte at the end.
    [Fact]
    public void LongText_WithABinaryByteAtTheEnd_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Bytes([.. Encoding.UTF8.GetBytes(new string('a', 100_000)), 0x00]), FileFormat.Text));

    // Same extension, other content: a picture, a presentation, Latin-1, a binary.
    [Fact]
    public void Png_NamedPdf_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Png(), FileFormat.Pdf));

    [Fact]
    public void Presentation_NamedDocx_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Zip("ppt/presentation.xml"), FileFormat.Docx));

    [Fact]
    public void BrokenZip_NamedDocx_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Bytes([0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6]), FileFormat.Docx));

    [Fact]
    public void Pdf_NamedDocx_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Pdf(), FileFormat.Docx));

    [Fact]
    public void Text_NamedPdf_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Bytes("Nur ein Text, kein PDF."u8.ToArray()), FileFormat.Pdf));

    [Fact]
    public void Latin1Text_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Bytes(Encoding.Latin1.GetBytes("Grüße")), FileFormat.Text));

    [Fact]
    public void Png_NamedTxt_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Png(), FileFormat.Text));

    [Fact]
    public void Docx_NamedMd_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Zip("word/document.xml"), FileFormat.Markdown));

    [Fact]
    public void Text_WithNul_DoesNotMatch() =>
        Assert.False(FileCheck.Matches(Bytes("Text\0mit Null"u8.ToArray()), FileFormat.Text));

    // Reads from the start, wherever the stream stood.
    [Fact]
    public void Check_RewindsTheStream()
    {
        var pdf = Pdf();
        pdf.Position = pdf.Length;

        Assert.True(FileCheck.Matches(pdf, FileFormat.Pdf));
    }

    private static MemoryStream Bytes(byte[] bytes) => new(bytes);

    private static MemoryStream Pdf() =>
        Bytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray());

    private static MemoryStream Png() =>
        Bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52]);

    // A zip with one entry, as Word or PowerPoint would write it at the least.
    private static MemoryStream Zip(string entry)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write("<?xml version=\"1.0\"?><document/>");
        }

        return stream;
    }
}