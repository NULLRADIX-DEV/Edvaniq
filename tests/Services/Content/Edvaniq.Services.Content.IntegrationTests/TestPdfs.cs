using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Edvaniq.Services.Content.IntegrationTests;

// PDFs with a text layer, written by the tests themselves from texts of the reference set, so no file of unknown origin
// lies in the repo. The built-in fonts of a PDF know no umlauts when writing, so the texts go in as ae, oe, ue and ss.
internal static class TestPdfs
{
    private const double FontSize = 10;

    private const double LineHeight = 14;

    private const double Top = 800;

    private const double Bottom = 60;

    // One column over as many pages as it takes. Returns the PDF and the text of every page as written.
    public static (byte[] Pdf, List<string> Pages) OneColumn(IEnumerable<string> paragraphs) =>
        Columns(paragraphs, [(50, 90)]);

    // Two columns side by side on every page, the left one filled first.
    public static (byte[] Pdf, List<string> Pages) TwoColumns(IEnumerable<string> paragraphs) =>
        Columns(paragraphs, [(50, 42), (310, 42)]);

    // Pages without any text, as a scan without a text layer.
    public static byte[] Empty(int pages)
    {
        var builder = new PdfDocumentBuilder();
        for (var page = 0; page < pages; page++)
        {
            builder.AddPage(PageSize.A4);
        }

        return builder.Build();
    }

    // A large file as a scanned script is one: pages of text and a few big pictures that make up most of its size.
    public static byte[] Large(int pages, int pictures, int linesPerPage)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var images = Enumerable.Range(0, pictures).Select(seed => NoisePng(1000, seed)).ToList();
        for (var number = 0; number < pages; number++)
        {
            var page = builder.AddPage(PageSize.A4);
            if (number % (pages / pictures) == 0 && number / (pages / pictures) < pictures)
            {
                page.AddPng(images[number / (pages / pictures)], new PdfRectangle(50, 400, 500, 780));
            }

            for (var line = 0; line < linesPerPage; line++)
            {
                page.AddText($"Seite {number + 1} Zeile {line + 1} ueber Saeuren und Basen.", FontSize, new PdfPoint(50, 380 - (line * LineHeight)), font);
            }
        }

        return builder.Build();
    }

    public static string Ascii(string text)
    {
        var ascii = new StringBuilder();
        foreach (var c in text)
        {
            ascii.Append(c switch
            {
                'ä' => "ae",
                'ö' => "oe",
                'ü' => "ue",
                'Ä' => "Ae",
                'Ö' => "Oe",
                'Ü' => "Ue",
                'ß' => "ss",
                _ when c is >= ' ' and <= '~' => c.ToString(),
                _ => " ",
            });
        }

        return ascii.ToString();
    }

    // The words of a text with single spaces, for comparing what was written with what was read.
    public static string Words(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static (byte[] Pdf, List<string> Pages) Columns(IEnumerable<string> paragraphs, (double X, int Width)[] columns)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var pages = new List<string>();
        PdfPageBuilder? page = null;
        var column = columns.Length;
        // Below the bottom, so the first line opens the first page.
        var y = double.MinValue;
        var written = new StringBuilder();
        foreach (var paragraph in paragraphs.Select(Ascii).Select(Words).Where(paragraph => paragraph.Length > 0))
        {
            foreach (var line in Wrap(paragraph, columns[Math.Min(column, columns.Length - 1)].Width))
            {
                if (y < Bottom)
                {
                    if (++column >= columns.Length)
                    {
                        if (page is not null)
                        {
                            pages.Add(written.ToString());
                            written.Clear();
                        }

                        page = builder.AddPage(PageSize.A4);
                        column = 0;
                    }

                    y = Top;
                }

                page!.AddText(line, FontSize, new PdfPoint(columns[column].X, y), font);
                written.Append(line).Append(' ');
                y -= LineHeight;
            }

            y -= LineHeight;
        }

        pages.Add(written.ToString());
        return (builder.Build(), pages);
    }

    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            line.Append(line.Length > 0 ? " " : "").Append(word);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return lines;
    }

    // An uncompressible picture, so the file gets as large as a real scan.
    private static byte[] NoisePng(int size, int seed)
    {
        var random = new Random(seed);
        using var rows = new MemoryStream();
        var row = new byte[size * 3];
        for (var y = 0; y < size; y++)
        {
            rows.WriteByte(0);
            random.NextBytes(row);
            rows.Write(row);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            rows.Position = 0;
            rows.CopyTo(zlib);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, size);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
        header[8] = 8;
        header[9] = 2;

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typed));
        png.Write(number);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}