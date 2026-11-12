using System.IO.Compression;
using System.Text;

namespace Edvaniq.Services.Content.Domain;

// The kinds of file a user can upload. Stored as text, so keep the names.
public enum FileFormat
{
    Pdf,
    Docx,
    Text,
    Markdown,
}

// What an uploaded file is, judged by its bytes. The extension says what the user claims, the content has to agree.
// What the browser sent as content type counts for nothing.
public static class FileCheck
{
    // 20 MB as the user counts them: what a file manager shows as 20 MB still passes.
    public const long MaxLength = 20L * 1024 * 1024;

    public const string TooLarge = "Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.";

    public const string Unsupported =
        "Dieser Dateityp wird nicht unterstützt. Lade eine Datei mit .pdf, .docx, .txt oder .md hoch.";

    public const string Mismatch =
        "Der Inhalt der Datei passt nicht zu ihrer Endung. Lade die Datei im richtigen Format hoch.";

    public const string Empty = "Die Datei ist leer. Lade eine Datei mit Inhalt hoch.";

    // A Word file with a password is no zip but an old compound file, as a .doc is. Either way it can't be read.
    public const string Protected =
        "Die Datei ist mit einem Passwort geschützt oder im alten Word-Format. Entferne den Schutz oder speichere sie als .docx und lade sie noch einmal hoch.";

    public const string PdfProtected =
        "Die Datei ist mit einem Passwort geschützt. Entferne den Schutz und lade sie noch einmal hoch.";

    public const string PackedTooTightly =
        "Die Datei lässt sich nicht lesen, weil sie ungewöhnlich stark gepackt ist. Speichere sie neu, zum Beispiel als PDF, und lade sie noch einmal hoch.";

    public const string TakesTooLong =
        "Die Datei braucht zu lange zum Lesen. Teile sie in kleinere Dateien auf und lade sie einzeln hoch.";

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    private static readonly byte[] CompoundMagic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    // The format the name claims, or null for anything else.
    public static FileFormat? FormatOf(string? fileName) =>
        Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
        {
            ".pdf" => FileFormat.Pdf,
            ".docx" => FileFormat.Docx,
            ".txt" => FileFormat.Text,
            ".md" or ".markdown" => FileFormat.Markdown,
            _ => null,
        };

    // Why the content can't be taken as the format, or null if it can. Needs a stream it can seek in.
    public static string? Refusal(Stream content, FileFormat format)
    {
        if (Matches(content, format))
        {
            return null;
        }

        content.Position = 0;
        return format == FileFormat.Docx && StartsWith(content, CompoundMagic) ? Protected : Mismatch;
    }

    // Whether the content is really of the format. Needs a stream it can seek in, reads it from the start.
    public static bool Matches(Stream content, FileFormat format)
    {
        content.Position = 0;
        return format switch
        {
            FileFormat.Pdf => StartsWith(content, PdfMagic),
            FileFormat.Docx => IsWordDocument(content),
            FileFormat.Text or FileFormat.Markdown => IsPlainText(content),
            _ => false,
        };
    }

    private static bool StartsWith(Stream content, byte[] magic)
    {
        Span<byte> head = stackalloc byte[magic.Length];
        return content.ReadAtLeast(head, magic.Length, throwOnEndOfStream: false) == magic.Length && head.SequenceEqual(magic);
    }

    // A zip with the main part of a Word document. A presentation or a spreadsheet is a zip too, but without it.
    private static bool IsWordDocument(Stream content)
    {
        if (!StartsWith(content, ZipMagic))
        {
            return false;
        }

        content.Position = 0;
        try
        {
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            return zip.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // Valid UTF-8, with or without a byte order mark, and no control characters but tab, line ends and form feed. A
    // binary file renamed to .txt fails on one or the other.
    private static bool IsPlainText(Stream content)
    {
        using var reader = new StreamReader(
            content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var buffer = new char[8192];
        try
        {
            int read;
            var first = true;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                var start = first && buffer[0] == '﻿' ? 1 : 0;
                first = false;
                for (var i = start; i < read; i++)
                {
                    var c = buffer[i];
                    if (char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\f'))
                    {
                        return false;
                    }
                }
            }
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return true;
    }
}