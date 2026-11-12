using System.Text;

using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// The worker has 256 MB without swap. A Word file near the upload limit of 20 MB, with a big picture it never opens and
// a text part heavy with formatting, has to stay well below that while it is read. Alone in its collection, so other
// tests don't blur the measure.
[Collection(MemoryCollection.Name)]
public sealed class WordTextMemoryTests
{
    // The same limit the PDF test sets.
    private const long Limit = 100L * 1024 * 1024;

    [Fact]
    public async Task LargeWordFile_IsReadWithinTheMemoryOfTheWorker()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using (var file = File.Create(path))
            {
                TestDocx.Write(file, FormattedRuns(), parts: new Dictionary<string, byte[]> { ["word/media/image1.png"] = Picture() });
            }

            Assert.InRange(new FileInfo(path).Length, 15L * 1024 * 1024, FileCheck.MaxLength);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var baseline = GC.GetTotalMemory(forceFullCollection: true);
            long peak = 0;
            using var done = new CancellationTokenSource();
            var sampler = Task.Run(async () =>
            {
                while (!done.IsCancellationRequested)
                {
                    peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: false));
                    await Task.Delay(5, CancellationToken.None);
                }
            }, TestContext.Current.CancellationToken);

            string text;
            await using (var file = File.OpenRead(path))
            {
                text = WordText.Of(file, TestContext.Current.CancellationToken);
            }

            await done.CancelAsync();
            await sampler;
            Assert.InRange(text.Length, Document.TextMaxLength / 2, Document.TextMaxLength);
            Assert.InRange(peak - baseline, long.MinValue, Limit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // About 40 MB of XML for 150,000 characters of text, every character in a run of its own with full formatting.
    private static string FormattedRuns()
    {
        const string run = """<w:r><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:b/><w:i/><w:color w:val="1F3864"/><w:sz w:val="24"/><w:szCs w:val="24"/><w:lang w:val="de-DE"/></w:rPr><w:t>a</w:t></w:r>""";
        var body = new StringBuilder();
        for (var paragraph = 0; paragraph < 1_500; paragraph++)
        {
            body.Append("<w:p>");
            for (var letter = 0; letter < 100; letter++)
            {
                body.Append(run);
            }

            body.Append("</w:p>");
        }

        return body.ToString();
    }

    // Stored without compression, so the file gets near the limit.
    private static byte[] Picture()
    {
        var bytes = new byte[16 * 1024 * 1024];
        new Random(320).NextBytes(bytes);
        return bytes;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MemoryCollection
{
    public const string Name = "memory";
}