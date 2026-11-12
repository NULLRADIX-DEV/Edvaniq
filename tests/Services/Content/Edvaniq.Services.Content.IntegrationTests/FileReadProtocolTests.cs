using System.Buffers.Binary;
using System.Text;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Files;

namespace Edvaniq.Services.Content.IntegrationTests;

// #318: what the reading process answers, and that the worker takes nothing but a complete, valid answer.
public sealed class FileReadProtocolTests
{
    [Fact]
    public void Pages_GoThereAndBack()
    {
        string[] pages = ["Erste Seite mit Umlauten äöü.", "", "Dritte Seite 🌱"];

        Assert.Equal(pages, FileReadProtocol.Read(Write(output => FileReadProtocol.WritePages(output, pages)))!.Pages);
    }

    // Every reason FileText can give comes back as the worker's own text.
    [Theory]
    [InlineData(Document.FileTextTooLong)]
    [InlineData(Document.TooManyPages)]
    [InlineData(FileCheck.PackedTooTightly)]
    [InlineData(FileCheck.PdfProtected)]
    [InlineData(FileCheck.TakesTooLong)]
    [InlineData(Document.FileNotProcessable)]
    [InlineData(Document.FileUnreadable)]
    public void Refusal_GoesThereAndBack(string reason)
    {
        var frame = Write(output => FileReadProtocol.WriteRefusal(output, FileReadProtocol.OutcomeOf(reason)));

        Assert.Equal(reason, FileReadProtocol.Read(frame)!.Reason);
    }

    [Fact]
    public void UnknownReason_IsUnreadable() =>
        Assert.Equal(FileReadOutcome.Unreadable, FileReadProtocol.OutcomeOf("Something the file said."));

    public static TheoryData<string, byte[]> Broken
    {
        get
        {
            var valid = Write(output => FileReadProtocol.WritePages(output, ["Seite"]));
            return new()
            {
                { "empty", [] },
                { "only the start", valid[..3] },
                { "cut off", valid[..^1] },
                { "bytes after the end", [.. valid, 0] },
                { "other version", [.. valid[..4], 2, .. valid[5..]] },
                { "unknown outcome", [.. "EDVQ"u8.ToArray(), FileReadProtocol.Version, 99] },
                { "refusal with a tail", [.. "EDVQ"u8.ToArray(), FileReadProtocol.Version, (byte)FileReadOutcome.Unreadable, 1] },
                { "more pages than allowed", [.. Head(), .. Int(Document.MaxPages + 1)] },
                { "page longer than the rest", [.. Head(), .. Int(1), .. Int(100), .. "kurz"u8.ToArray()] },
                { "no UTF-8", [.. Head(), .. Int(1), .. Int(2), 0xC3, 0x28] },
                { "more text than a document may have", TooMuchText() },
            };
        }
    }

    [Theory]
    [MemberData(nameof(Broken))]
    public void BrokenAnswer_IsNoAnswer(string because, byte[] frame) =>
        Assert.True(FileReadProtocol.Read(frame) is null, because);

    private static byte[] Write(Action<Stream> write)
    {
        using var output = new MemoryStream();
        write(output);
        return output.ToArray();
    }

    private static byte[] Head() => [.. "EDVQ"u8.ToArray(), FileReadProtocol.Version, (byte)FileReadOutcome.Pages];

    private static byte[] Int(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] TooMuchText()
    {
        var page = Encoding.UTF8.GetBytes(new string('a', Document.TextMaxLength + 1));
        return [.. Head(), .. Int(1), .. Int(page.Length), .. page];
    }
}