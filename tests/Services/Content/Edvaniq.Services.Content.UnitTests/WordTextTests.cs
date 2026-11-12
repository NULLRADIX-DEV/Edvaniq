using System.Text;

using Edvaniq.Services.Content.Domain;

using static Edvaniq.Services.Content.UnitTests.TestDocx;

namespace Edvaniq.Services.Content.UnitTests;

// #320: the text of a Word file with its headings for the splitter, and nothing but what a reader sees as running text.
public sealed class WordTextTests
{
    [Fact]
    public void Headings_ComeAsMarkdown_ByNameOutlineLevelOrBase_ParagraphsApart()
    {
        var text = Read(
            P("Zellbiologie", "berschrift1")
            + P("Die Zelle ist die kleinste Einheit des Lebens.")
            + P("Aufbau", "Unterkapitel")
            + P("Der Zellkern enthält die Erbinformation.", "Standard")
            + P("Stoffwechsel", "Kapitel")
            + """<w:p><w:pPr><w:outlineLvl w:val="2"/></w:pPr><w:r><w:t>Atmung</w:t></w:r></w:p>"""
            + P("Mitochondrien liefern Energie."));

        Assert.Equal(
            "# Zellbiologie\n\nDie Zelle ist die kleinste Einheit des Lebens.\n\n## Aufbau\n\nDer Zellkern enthält die Erbinformation."
            + "\n\n# Stoffwechsel\n\n### Atmung\n\nMitochondrien liefern Energie.",
            text);
    }

    // English Word, and a file without any styles.
    [Fact]
    public void EnglishStyles_AndNoStylesAtAll_Work()
    {
        const string styles = """<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/></w:style>""";

        Assert.Equal("# Cells\n\nText.", Read(P("Cells", "Heading1") + P("Text."), styles));
        Assert.Equal("Cells\n\nText.", Read(P("Cells", "Heading1") + P("Text."), styles: null));
    }

    [Fact]
    public void RunsOfAParagraph_AreJoined_TabsAndBreaksKept()
    {
        var text = Read("""
            <w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Foto</w:t></w:r><w:r><w:t xml:space="preserve">synthese </w:t></w:r>
            <w:r><w:tab/><w:t>braucht Licht.</w:t><w:br/><w:t>Neue Zeile.</w:t></w:r></w:p>
            """);

        Assert.Equal("Fotosynthese  braucht Licht.\nNeue Zeile.", text);
    }

    // Fields go with code and result: a table of contents would repeat every heading, a page number means nothing.
    [Fact]
    public void Fields_LeaveNothing()
    {
        var text = Read(
            """
            <w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText>TOC \o "1-3"</w:instrText></w:r>
            <w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>Zellbiologie 1</w:t></w:r></w:p>
            <w:p><w:r><w:t>Aufbau 2</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            <w:p><w:fldSimple w:instr="PAGE"><w:r><w:t>7</w:t></w:r></w:fldSimple><w:r><w:t>Seite</w:t></w:r></w:p>
            <w:p><w:r><w:t xml:space="preserve">Kosten: </w:t></w:r><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText>INCLUDETEXT "http://example.org/x"</w:instrText></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
            """
            + P("Die Zelle."));

        Assert.Equal("Seite\n\nKosten:\n\nDie Zelle.", text);
    }

    [Fact]
    public void HiddenDeletedAndInsertedText_AreTakenAsTheReaderSeesThem()
    {
        var text = Read("""
            <w:p><w:r><w:t xml:space="preserve">Sichtbar </w:t></w:r><w:r><w:rPr><w:vanish/></w:rPr><w:t>versteckt</w:t></w:r>
            <w:r><w:rPr><w:vanish w:val="0"/></w:rPr><w:t>wieder da</w:t></w:r>
            <w:del w:id="1" w:author="A"><w:r><w:delText>gelöscht</w:delText></w:r></w:del>
            <w:ins w:id="2" w:author="A"><w:r><w:t xml:space="preserve"> eingefügt</w:t></w:r></w:ins></w:p>
            <w:p><w:pPr><w:rPr><w:vanish/></w:rPr></w:pPr><w:r><w:t>Absatz mit verstecktem Absatzzeichen.</w:t></w:r></w:p>
            """);

        Assert.Equal("Sichtbar wieder da eingefügt\n\nAbsatz mit verstecktem Absatzzeichen.", text);
    }

    // Embedded objects, drawings, text boxes and tables bring no text, and the parts behind them are never opened: a
    // macro project or an object that isn't even valid can't break the reading.
    [Fact]
    public void ObjectsDrawingsMacrosAndTables_BringNoText()
    {
        var body = P("Vorher.")
            + """<w:p><w:r><w:object><v:shape id="x"><v:imagedata r:id="rId9"/></v:shape><o:OLEObject ProgID="Excel.Sheet.12" r:id="rId10"/></w:object></w:r></w:p>"""
            + """<w:p><w:r><w:drawing><w:t>im Bild</w:t></w:drawing></w:r></w:p>"""
            + """<w:p><w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:txbxContent><w:p><w:r><w:t>Textfeld</w:t></w:r></w:p></w:txbxContent></mc:Choice><mc:Fallback><w:pict><w:t>Textfeld</w:t></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>"""
            + """<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Zelle A1</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"""
            + P("Nachher.");
        var parts = new Dictionary<string, byte[]>
        {
            ["word/vbaProject.bin"] = [0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3],
            ["word/embeddings/oleObject1.bin"] = Encoding.UTF8.GetBytes("<kaputt"),
            ["word/_rels/document.xml.rels"] = Encoding.UTF8.GetBytes(
                """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId10" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" Target="http://example.org/boese.bin" TargetMode="External"/></Relationships>"""),
        };

        Assert.Equal("Vorher.\n\nNachher.", Read(body, parts: parts));
    }

    // A link keeps the text the reader sees, its target stays in the package.
    [Fact]
    public void Hyperlink_KeepsItsText_NotItsTarget()
    {
        var text = Read("""<w:p><w:r><w:t xml:space="preserve">Mehr bei </w:t></w:r><w:hyperlink r:id="rId5"><w:r><w:t>Wikipedia</w:t></w:r></w:hyperlink></w:p>""");

        Assert.Equal("Mehr bei Wikipedia", text);
    }

    [Fact]
    public void MoreTextThanAllowed_IsRefused()
    {
        var body = string.Concat(Enumerable.Repeat(P(new string('a', 999)), Document.TextMaxLength / 1000 + 1));

        Assert.Equal(Document.FileTextTooLong, Assert.Throws<FileRefusedException>(() => Read(body)).Reason);
    }

    // The zip's header could claim anything, the bytes are counted while they come.
    [Fact]
    public void DocumentThatUnpacksBeyondTheLimit_IsRefused()
    {
        using var stream = new MemoryStream(Bomb(WordText.DocumentMaxLength));

        var refused = Assert.Throws<FileRefusedException>(() => WordText.Of(stream, TestContext.Current.CancellationToken));

        Assert.Equal(Document.FileUnreadable, refused.Reason);
    }

    [Fact]
    public void PackageWithoutDocument_IsRefused()
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("word/styles.xml");
        }

        stream.Position = 0;
        Assert.Equal(Document.FileUnreadable, Assert.Throws<FileRefusedException>(() => WordText.Of(stream, TestContext.Current.CancellationToken)).Reason);
    }

    // A DTD could pull in other files or blow up in entities. It never gets read.
    [Fact]
    public void BrokenOrDtdXml_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Read("<w:p><w:r><w:t>offen"));

        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("word/document.xml").Open();
            entry.Write(Encoding.UTF8.GetBytes("""<?xml version="1.0"?><!DOCTYPE d [<!ENTITY x SYSTEM "file:///etc/passwd">]><d>&x;</d>"""));
        }

        stream.Position = 0;
        Assert.ThrowsAny<System.Xml.XmlException>(() => WordText.Of(stream, TestContext.Current.CancellationToken));
    }

    private static string Read(string body, string? styles = TestDocx.Styles, IReadOnlyDictionary<string, byte[]>? parts = null)
    {
        using var stream = new MemoryStream(Of(body, styles, parts));
        return WordText.Of(stream, TestContext.Current.CancellationToken);
    }
}