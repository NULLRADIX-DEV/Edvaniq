using System.Text;

using Edvaniq.Services.Content.Domain;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// The text of an uploaded file, one entry per page. A PDF gives its text layer page by page in reading order: words, then
// blocks of lines, then the blocks in the order a reader takes them, so two columns come one after the other. A text or
// Markdown file is one page, as it is. Gives up early on more pages or text than a document may have, so a large file
// never gets read in full. Pictures, tables and formulas count for nothing, a scan has no text layer and comes out empty.
public static class FileText
{
    public static IReadOnlyList<string> PagesOf(Stream content, FileFormat format, CancellationToken cancellationToken) =>
        format switch
        {
            FileFormat.Pdf => PdfPages(content, cancellationToken),
            FileFormat.Text or FileFormat.Markdown => [PlainText(content)],
            FileFormat.Docx => throw new FileRefusedException(Document.WordNotYet),
            _ => throw new NotSupportedException($"Reading {format} files is unknown."),
        };

    private static List<string> PdfPages(Stream content, CancellationToken cancellationToken)
    {
        using var pdf = PdfDocument.Open(content, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        if (pdf.NumberOfPages > Document.MaxPages)
        {
            throw new FileRefusedException(Document.TooManyPages);
        }

        var pages = new List<string>(pdf.NumberOfPages);
        var length = 0;
        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = TextOf(pdf.GetPage(number));
            length += text.Length + Document.PageBreak.Length;
            if (length > Document.TextMaxLength + Document.PageBreak.Length)
            {
                throw new FileRefusedException(Document.FileTextTooLong);
            }

            pages.Add(text);
        }

        return pages;
    }

    // The blocks in reading order with a blank line between them, so each stays a paragraph of its own.
    private static string TextOf(Page page)
    {
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters);
        var blocks = UnsupervisedReadingOrderDetector.Instance.Get(DocstrumBoundingBoxes.Instance.GetBlocks(words));
        return string.Join("\n\n", blocks.Select(Paragraph).Where(paragraph => paragraph.Length > 0));
    }

    // The lines of a block, joined to paragraphs. A block may hold several when they are close, so a gap of more than one
    // and a half line heights starts a new one, as an empty line does. A hyphen at the end of a line stays,
    // "Licht- und Schattenblätter" can't be told apart from a split word, and the AI reads "Foto- synthese" well enough.
    private static string Paragraph(TextBlock block)
    {
        var text = new StringBuilder();
        TextLine? previous = null;
        foreach (var line in block.TextLines.Where(line => line.Text.Trim().Length > 0))
        {
            if (previous is not null)
            {
                var gap = previous.BoundingBox.Bottom - line.BoundingBox.Top;
                var height = (previous.BoundingBox.Height + line.BoundingBox.Height) / 2;
                text.Append(gap > ParagraphGap * height ? "\n\n" : " ");
            }

            text.Append(line.Text.Trim());
            previous = line;
        }

        return text.ToString();
    }

    private const double ParagraphGap = 1.5;

    // UTF-8 as FileCheck accepted it, read only one character beyond the limit.
    private static string PlainText(Stream content)
    {
        using var reader = new StreamReader(content, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var buffer = new char[Document.TextMaxLength + 1];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return read > Document.TextMaxLength ? throw new FileRefusedException(Document.FileTextTooLong) : new string(buffer, 0, read);
    }
}

// A file the document can't take, with the reason the user sees.
public sealed class FileRefusedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}