using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Edvaniq.Services.Content.Domain;

// The text of a Word file (.docx), in the form the splitter reads: a heading as "# " to "###### ", paragraphs apart by
// a blank line. Only two parts of the package are ever opened, the text and the styles, both streamed, so macros,
// embedded objects, pictures and links to the outside are never loaded, let alone run. Of the text only what a reader
// sees as running text counts: no fields (code nor result, a table of contents would repeat every heading), no hidden
// or deleted text, no drawings, objects or text boxes, and no tables. A link keeps its text, never its target.
public static class WordText
{
    // Bytes a part may unpack to. The size in the zip's header is the packer's word, so the bytes are counted while they
    // come. Far beyond what 200,000 characters of text need, even with heavy formatting.
    public const long DocumentMaxLength = 64L * 1024 * 1024;

    public const long StylesMaxLength = 8L * 1024 * 1024;

    // Never the content of these, wherever they are: tables, deleted text, drawings and objects of either kind, simple
    // fields, and markup Word keeps for other versions of itself.
    private static readonly HashSet<string> Skipped = ["tbl", "del", "drawing", "pict", "object", "fldSimple", "instrText", "delText"];

    public static string Of(Stream content, CancellationToken cancellationToken)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var document = zip.GetEntry("word/document.xml") ?? throw new FileRefusedException(Document.FileUnreadable);
        var levels = zip.GetEntry("word/styles.xml") is { } styles ? HeadingLevels(styles) : [];
        return Text(document, levels, cancellationToken);
    }

    // The heading level of every paragraph style that has one: its own outline level, else "heading n" as its name,
    // else the level of the style it is based on. The name, not the id: German Word stores "berschrift1" as id, but
    // keeps "heading 1" as name.
    private static Dictionary<string, int> HeadingLevels(ZipArchiveEntry entry)
    {
        var styles = new Dictionary<string, (int? Level, string? BasedOn)>(StringComparer.Ordinal);
        using (var reader = Reader(entry, StylesMaxLength))
        {
            reader.MoveToContent();
            var main = reader.NamespaceURI;
            string? id = null;
            int? level = null;
            string? basedOn = null;
            while (reader.Read())
            {
                if (reader.NamespaceURI != main)
                {
                    continue;
                }

                if (reader is { NodeType: XmlNodeType.Element, LocalName: "style" })
                {
                    if (id is not null)
                    {
                        styles[id] = (level, basedOn);
                    }

                    (id, level, basedOn) = (reader.GetAttribute("styleId", main), null, null);
                    if (reader.IsEmptyElement)
                    {
                        styles[id ?? ""] = (null, null);
                        id = null;
                    }
                }
                else if (reader.NodeType == XmlNodeType.Element && id is not null)
                {
                    var value = reader.GetAttribute("val", main);
                    switch (reader.LocalName)
                    {
                        case "name":
                            level ??= NamedLevel(value);
                            break;
                        case "outlineLvl":
                            level = OutlineLevel(value) ?? level;
                            break;
                        case "basedOn":
                            basedOn = value;
                            break;
                    }
                }
                else if (reader is { NodeType: XmlNodeType.EndElement, LocalName: "style" } && id is not null)
                {
                    styles[id] = (level, basedOn);
                    id = null;
                }
            }
        }

        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var style in styles.Keys)
        {
            // A chain of based-on styles is short; the limit only stops a loop in a crafted file.
            var current = style;
            for (var step = 0; step < 16 && current is not null && styles.TryGetValue(current, out var found); step++)
            {
                if (found.Level is { } level)
                {
                    levels[style] = level;
                    break;
                }

                current = found.BasedOn;
            }
        }

        return levels;
    }

    private static string Text(ZipArchiveEntry entry, IReadOnlyDictionary<string, int> levels, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var paragraph = new StringBuilder();
        string? style = null;
        int? outline = null;
        var inParagraphProperties = false;
        var inRunProperties = false;
        var hiddenRun = false;
        var fields = 0;

        using var reader = Reader(entry, DocumentMaxLength);
        // The namespace of the root, not a fixed one: Word writes the transitional one, "strict" files another.
        reader.MoveToContent();
        var main = reader.NamespaceURI;
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element
                && ((reader.NamespaceURI == main && Skipped.Contains(reader.LocalName))
                    || (reader.NamespaceURI != main && reader.LocalName == "AlternateContent")))
            {
                reader.Skip();
                continue;
            }

            if (reader.NamespaceURI == main && reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "p":
                        paragraph.Clear();
                        (style, outline) = (null, null);
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            continue;
                        }

                        break;
                    case "pPr":
                        inParagraphProperties = !reader.IsEmptyElement;
                        break;
                    case "rPr":
                        inRunProperties = !inParagraphProperties && !reader.IsEmptyElement;
                        break;
                    case "r":
                        hiddenRun = false;
                        break;
                    case "pStyle" when inParagraphProperties:
                        style = reader.GetAttribute("val", main);
                        break;
                    case "outlineLvl" when inParagraphProperties:
                        outline = OutlineLevel(reader.GetAttribute("val", main));
                        break;
                    case "vanish" when inRunProperties:
                        hiddenRun = IsOn(reader.GetAttribute("val", main));
                        break;
                    case "fldChar":
                        var type = reader.GetAttribute("fldCharType", main);
                        fields = type == "begin" ? fields + 1 : type == "end" ? Math.Max(0, fields - 1) : fields;
                        break;
                    case "t" when !hiddenRun && fields == 0:
                        paragraph.Append(reader.ReadElementContentAsString());
                        if (text.Length + paragraph.Length > Document.TextMaxLength)
                        {
                            throw new FileRefusedException(Document.FileTextTooLong);
                        }

                        continue;
                    case "tab" when !hiddenRun && fields == 0 && !inParagraphProperties:
                        paragraph.Append(' ');
                        break;
                    case "br" or "cr" when !hiddenRun && fields == 0:
                        paragraph.Append('\n');
                        break;
                }
            }
            else if (reader.NamespaceURI == main && reader.NodeType == XmlNodeType.EndElement)
            {
                switch (reader.LocalName)
                {
                    case "pPr":
                        inParagraphProperties = false;
                        break;
                    case "rPr":
                        inRunProperties = false;
                        break;
                    case "p":
                        cancellationToken.ThrowIfCancellationRequested();
                        Append(text, paragraph, outline ?? (style is not null && levels.TryGetValue(style, out var level) ? level : null));
                        break;
                }
            }

            reader.Read();
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, StringBuilder paragraph, int? level)
    {
        var content = paragraph.ToString().Trim();
        if (content.Length == 0)
        {
            return;
        }

        if (text.Length > 0)
        {
            text.Append("\n\n");
        }

        if (level is { } heading)
        {
            text.Append('#', heading).Append(' ').Append(content.ReplaceLineEndings(" "));
        }
        else
        {
            text.Append(content);
        }

        if (text.Length > Document.TextMaxLength)
        {
            throw new FileRefusedException(Document.FileTextTooLong);
        }
    }

    // Outline levels 0 to 8 are headings 1 to 9, the splitter knows six. 9 is body text.
    private static int? OutlineLevel(string? value) =>
        int.TryParse(value, out var level) && level is >= 0 and <= 8 ? Math.Min(level + 1, 6) : null;

    private static int? NamedLevel(string? name) =>
        name is not null && name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(8), out var level)
            && level is >= 1 and <= 9
            ? Math.Min(level, 6)
            : null;

    // A toggle without a value is on.
    private static bool IsOn(string? value) => value is null or "1" or "true" or "on";

    private static XmlReader Reader(ZipArchiveEntry entry, long maxLength) =>
        XmlReader.Create(
            new CappedStream(entry.Open(), maxLength),
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                CloseInput = true,
            });

    // Reads at most maxLength bytes from inner, then refuses the file.
    private sealed class CappedStream(Stream inner, long maxLength) : Stream
    {
        private long read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Count(int bytes)
        {
            read += bytes;
            return read > maxLength ? throw new FileRefusedException(Document.FileUnreadable) : bytes;
        }
    }
}