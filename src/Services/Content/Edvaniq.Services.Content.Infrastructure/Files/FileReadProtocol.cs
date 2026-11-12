using System.Buffers.Binary;
using System.Text;

using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// How the reading process tells the worker what came out of a file, on its standard output. Binary and length prefixed,
// so text from a file can never pass for a frame. A refusal is a code the worker turns into its own text, the file never
// chooses what the user reads.
//
// Frame: "EDVQ", version, outcome, then for Pages an int32 count and per page an int32 byte length and the page as UTF-8.
// All numbers big endian.
public static class FileReadProtocol
{
    public const byte Version = 1;

    private static readonly byte[] Magic = "EDVQ"u8.ToArray();

    // What the worker reads at most: the most text a document may have, four bytes per character in the worst case, plus
    // the frame around it. Anything longer is no valid answer.
    public static readonly long MaxLength = Magic.Length + 2 + 4 + (((long)Document.TextMaxLength * 4) + ((long)Document.MaxPages * 4));

    // Each refusal of FileText, with its code. The code is the contract between the two processes, keep the numbers.
    private static readonly (FileReadOutcome Outcome, string Reason)[] Reasons =
    [
        (FileReadOutcome.TextTooLong, Document.FileTextTooLong),
        (FileReadOutcome.TooManyPages, Document.TooManyPages),
        (FileReadOutcome.PackedTooTightly, FileCheck.PackedTooTightly),
        (FileReadOutcome.Protected, FileCheck.PdfProtected),
        (FileReadOutcome.TakesTooLong, FileCheck.TakesTooLong),
        (FileReadOutcome.TooLarge, Document.FileNotProcessable),
        (FileReadOutcome.Unreadable, Document.FileUnreadable),
    ];

    public static void WritePages(Stream output, IReadOnlyList<string> pages)
    {
        WriteHead(output, FileReadOutcome.Pages);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, pages.Count);
        output.Write(number);
        foreach (var page in pages)
        {
            var bytes = Encoding.UTF8.GetBytes(page);
            BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
            output.Write(number);
            output.Write(bytes);
        }

        output.Flush();
    }

    public static void WriteRefusal(Stream output, FileReadOutcome outcome)
    {
        if (outcome == FileReadOutcome.Pages)
        {
            throw new ArgumentException("A refusal needs a reason.", nameof(outcome));
        }

        WriteHead(output, outcome);
        output.Flush();
    }

    // The code for a reason FileText gave, Unreadable for any other.
    public static FileReadOutcome OutcomeOf(string reason) =>
        Reasons.FirstOrDefault(entry => entry.Reason == reason) is { Reason: not null } found ? found.Outcome : FileReadOutcome.Unreadable;

    public static string ReasonOf(FileReadOutcome outcome) =>
        Reasons.FirstOrDefault(entry => entry.Outcome == outcome) is { Reason: not null } found ? found.Reason : Document.FileUnreadable;

    // The pages, or the reason the file was refused. Null if the bytes are no complete, valid frame.
    public static FileReadResult? Read(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < Magic.Length + 2 || !frame[..Magic.Length].SequenceEqual(Magic) || frame[Magic.Length] != Version)
        {
            return null;
        }

        var outcome = (FileReadOutcome)frame[Magic.Length + 1];
        var rest = frame[(Magic.Length + 2)..];
        if (outcome != FileReadOutcome.Pages)
        {
            return Enum.IsDefined(outcome) && rest.IsEmpty ? FileReadResult.Refused(ReasonOf(outcome)) : null;
        }

        if (!TryTake(ref rest, out var count) || count < 0 || count > Document.MaxPages)
        {
            return null;
        }

        var pages = new List<string>(count);
        var text = 0;
        for (var index = 0; index < count; index++)
        {
            if (!TryTake(ref rest, out var length) || length < 0 || length > rest.Length)
            {
                return null;
            }

            string page;
            try
            {
                page = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(rest[..length]);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }

            text += page.Length;
            if (text > Document.TextMaxLength)
            {
                return null;
            }

            pages.Add(page);
            rest = rest[length..];
        }

        return rest.IsEmpty ? FileReadResult.Read(pages) : null;
    }

    private static void WriteHead(Stream output, FileReadOutcome outcome)
    {
        output.Write(Magic);
        output.WriteByte(Version);
        output.WriteByte((byte)outcome);
    }

    private static bool TryTake(ref ReadOnlySpan<byte> rest, out int number)
    {
        if (rest.Length < 4)
        {
            number = 0;
            return false;
        }

        number = BinaryPrimitives.ReadInt32BigEndian(rest);
        rest = rest[4..];
        return true;
    }
}

public enum FileReadOutcome : byte
{
    Pages = 0,
    TextTooLong = 1,
    TooManyPages = 2,
    PackedTooTightly = 3,
    Protected = 4,
    TakesTooLong = 5,
    TooLarge = 6,
    Unreadable = 7,
}

// What came out of a file: its pages, or the reason the user reads instead.
public sealed record FileReadResult(IReadOnlyList<string>? Pages, string? Reason)
{
    public static FileReadResult Read(IReadOnlyList<string> pages) => new(pages, null);

    public static FileReadResult Refused(string reason) => new(null, reason);
}