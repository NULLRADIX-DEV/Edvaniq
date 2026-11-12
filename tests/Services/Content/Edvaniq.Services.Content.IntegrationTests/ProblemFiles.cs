using System.IO.Compression;
using System.Text;

namespace Edvaniq.Services.Content.IntegrationTests;

// Broken and hostile files for #318, written by the tests byte by byte, so no such file lies in the repo. Each PDF has a
// correct cross-reference table unless it is meant to be broken.
internal static class ProblemFiles
{
    // One page whose content stream unpacks to this many spaces. A few MB packed, gigabytes unpacked if asked.
    public static byte[] PackedPdf(long unpackedLength)
    {
        using var packed = new MemoryStream();
        packed.Write([0x78, 0x9C]);
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var spaces = new byte[1024 * 1024];
            Array.Fill(spaces, (byte)' ');
            for (var left = unpackedLength; left > 0; left -= spaces.Length)
            {
                deflate.Write(spaces, 0, (int)Math.Min(spaces.Length, left));
            }
        }

        return Pdf(Page(), Stream(packed.ToArray(), "/Filter /FlateDecode"));
    }

    // A file with a user password. Without it nothing can be read.
    public static byte[] EncryptedPdf()
    {
        var o = new string('A', 64);
        var u = new string('B', 64);
        return Pdf(
            [Page(), Stream("BT ET"u8.ToArray(), ""), $"<< /Filter /Standard /V 1 /R 2 /O <{o}> /U <{u}> /P -4 >>"],
            trailer: "/Encrypt 5 0 R /ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]");
    }

    // A whole PDF cut off in the middle, as an upload that broke off.
    public static byte[] TruncatedPdf()
    {
        var whole = Pdf(Page(), Stream("BT /F1 12 Tf 50 700 Td (Photosynthese) Tj ET"u8.ToArray(), ""));
        return whole[..(whole.Length / 2)];
    }

    // Starts like a PDF and then is noise.
    public static byte[] GarbagePdf()
    {
        var noise = new byte[64 * 1024];
        new Random(318).NextBytes(noise);
        return [.. "%PDF-1.7\n"u8.ToArray(), .. noise];
    }

    private static string Page() =>
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R >>";

    private static string Stream(byte[] data, string filter) =>
        $"<< /Length {data.Length} {filter} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream";

    private static byte[] Pdf(string page, string content) => Pdf([page, content]);

    // Objects 1 and 2 are catalog and page tree, the given ones follow from 3 on.
    private static byte[] Pdf(string[] objects, string trailer = "")
    {
        string[] all = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", .. objects];
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < all.Length; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{all[i]}\nendobj\n");
        }

        var xref = Encoding.Latin1.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {all.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {all.Length + 1} /Root 1 0 R {trailer} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }
}