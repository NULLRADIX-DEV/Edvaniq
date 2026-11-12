using System.IO.Compression;
using System.Security;
using System.Text;

namespace Edvaniq.Services.Content.UnitTests;

// Word files written by the tests, as small as a .docx can be: the parts Word itself writes for text and styles, plus
// whatever a test adds (a macro project, an embedded object). Styles named like German Word names them: the id is
// localized, the name stays English.
public static class TestDocx
{
    public const string Styles = """
        <w:style w:type="paragraph" w:styleId="Standard"><w:name w:val="Normal"/></w:style>
        <w:style w:type="paragraph" w:styleId="berschrift1"><w:name w:val="heading 1"/><w:basedOn w:val="Standard"/></w:style>
        <w:style w:type="paragraph" w:styleId="berschrift2"><w:name w:val="heading 2"/><w:basedOn w:val="Standard"/></w:style>
        <w:style w:type="paragraph" w:styleId="Kapitel"><w:name w:val="Kapitel"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style>
        <w:style w:type="paragraph" w:styleId="Unterkapitel"><w:name w:val="Unterkapitel"/><w:basedOn w:val="berschrift2"/></w:style>
        """;

    private const string Namespaces =
        """xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" """
        + """xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" """
        + """xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" """
        + """xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:v="urn:schemas-microsoft-com:vml" """;

    // A paragraph of one run, with a style if given.
    public static string P(string text, string? style = null) =>
        $"""<w:p>{(style is null ? "" : $"""<w:pPr><w:pStyle w:val="{style}"/></w:pPr>""")}<w:r><w:t xml:space="preserve">{SecurityElement.Escape(text)}</w:t></w:r></w:p>""";

    public static byte[] Of(string body, string? styles = Styles, IReadOnlyDictionary<string, byte[]>? parts = null)
    {
        using var stream = new MemoryStream();
        Write(stream, body, styles, parts);
        return stream.ToArray();
    }

    public static void Write(Stream stream, string body, string? styles = Styles, IReadOnlyDictionary<string, byte[]>? parts = null)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Add(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/></Types>""");
        Add(zip, "word/document.xml", $"""<?xml version="1.0" encoding="UTF-8"?><w:document {Namespaces}><w:body>{body}</w:body></w:document>""");
        if (styles is not null)
        {
            Add(zip, "word/styles.xml", $"""<?xml version="1.0" encoding="UTF-8"?><w:styles {Namespaces}>{styles}</w:styles>""");
        }

        foreach (var (name, content) in parts ?? new Dictionary<string, byte[]>())
        {
            using var entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
            entry.Write(content);
        }
    }

    // A document.xml that unpacks to more than length bytes, from a zip a fraction of that.
    public static byte[] Bomb(long length)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("word/document.xml", CompressionLevel.SmallestSize).Open();
            entry.Write(Encoding.UTF8.GetBytes($"<w:document {Namespaces}><w:body><w:p>"));
            var padding = Encoding.UTF8.GetBytes(new string(' ', 1024 * 1024));
            for (long written = 0; written <= length; written += padding.Length)
            {
                entry.Write(padding);
            }

            entry.Write(Encoding.UTF8.GetBytes("</w:p></w:body></w:document>"));
        }

        return stream.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var entry = zip.CreateEntry(name).Open();
        entry.Write(Encoding.UTF8.GetBytes(content));
    }
}