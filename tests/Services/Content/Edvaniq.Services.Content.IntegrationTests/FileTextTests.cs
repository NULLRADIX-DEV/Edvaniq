using System.Text;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Files;
using Edvaniq.Services.Content.UnitTests;

namespace Edvaniq.Services.Content.IntegrationTests;

// #282: the text of a PDF page by page, in reading order, from PDFs written out of the reference set. Text and Markdown
// files are one page. No database needed.
public sealed class FileTextTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // AC1 and AC2, one column: every page has its own text, in the order it was written, and the page numbers match.
    [Fact]
    public void OneColumn_GivesEachPageItsText_InOrder()
    {
        var (pdf, written) = TestPdfs.OneColumn(Paragraphs("biologie-fotosynthese"));
        Assert.True(written.Count >= 2, "The reference text should fill more than one page.");

        var pages = Read(pdf, FileFormat.Pdf);

        Assert.Equal(written.Select(TestPdfs.Words), pages.Select(TestPdfs.Words));
    }

    // AC2, two columns: the left column comes before the right one, on every page.
    [Fact]
    public void TwoColumns_AreReadOneAfterTheOther()
    {
        var (pdf, written) = TestPdfs.TwoColumns(Paragraphs("informatik-sortierverfahren"));

        var pages = Read(pdf, FileFormat.Pdf);

        Assert.Equal(written.Select(TestPdfs.Words), pages.Select(TestPdfs.Words));
    }

    // Paragraphs stay apart, so the splitter finds them.
    [Fact]
    public void Paragraphs_StayApart()
    {
        var (pdf, _) = TestPdfs.OneColumn(["Erster Absatz mit genug Worten.", "Zweiter Absatz mit genug Worten."]);

        Assert.Equal("Erster Absatz mit genug Worten.\n\nZweiter Absatz mit genug Worten.", Assert.Single(Read(pdf, FileFormat.Pdf)));
    }

    // A scan has no text layer: its pages are there, but empty.
    [Fact]
    public void PagesWithoutText_AreEmpty() =>
        Assert.Equal(["", ""], Read(TestPdfs.Empty(2), FileFormat.Pdf));

    [Fact]
    public void MorePagesThanAllowed_AreRefusedBeforeReading()
    {
        var refused = Assert.Throws<FileRefusedException>(() => Read(TestPdfs.Empty(Document.MaxPages + 1), FileFormat.Pdf));

        Assert.Equal(Document.TooManyPages, refused.Reason);
    }

    [Fact]
    public void MoreTextThanAllowed_IsRefused()
    {
        var refused = Assert.Throws<FileRefusedException>(() => Read(TestPdfs.Large(pages: 120, pictures: 1, linesPerPage: 40), FileFormat.Pdf));

        Assert.Equal(Document.FileTextTooLong, refused.Reason);
    }

    [Fact]
    public void BrokenPdf_Throws() =>
        Assert.ThrowsAny<Exception>(() => Read("%PDF-1.4\nnichts weiter"u8.ToArray(), FileFormat.Pdf));

    // UTF-8 as uploaded, with umlauts and the Markdown headings the splitter knows.
    [Theory]
    [InlineData(FileFormat.Text)]
    [InlineData(FileFormat.Markdown)]
    public void TextFile_IsOnePage_AsItIs(FileFormat format)
    {
        const string text = "# Säuren\n\nEine Säure gibt ein Proton ab.";

        Assert.Equal([text], Read(Encoding.UTF8.GetBytes(text), format));
    }

    [Fact]
    public void TextFile_OverTheLimit_IsRefused()
    {
        var refused = Assert.Throws<FileRefusedException>(() => Read(Encoding.UTF8.GetBytes(new string('a', Document.TextMaxLength + 1)), FileFormat.Text));

        Assert.Equal(Document.FileTextTooLong, refused.Reason);
    }

    private static List<string> Paragraphs(string id) =>
        [.. ReferenceSet.Load().Documents.Single(document => document.Id == id).Text.ReplaceLineEndings("\n").Split("\n\n")];

    private static IReadOnlyList<string> Read(byte[] content, FileFormat format)
    {
        using var stream = new MemoryStream(content);
        return FileText.PagesOf(stream, format, Cancel);
    }
}

// The worker has 256 MB without swap. A PDF near the upload limit of 20 MB has to stay well below that while it is read,
// or the worker dies and the document waits forever. Alone in its collection, so other tests don't blur the measure.
[Collection(MemoryCollection.Name)]
public sealed class FileTextMemoryTests
{
    // Measured 19 MB for 18 MB and 300 pages, this leaves room and still fails long before the worker would.
    private const long Limit = 100L * 1024 * 1024;

    [Fact]
    public async Task LargePdf_IsReadWithinTheMemoryOfTheWorker()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, TestPdfs.Large(pages: Document.MaxPages, pictures: 6, linesPerPage: 10), TestContext.Current.CancellationToken);
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
            });

            IReadOnlyList<string> pages;
            await using (var file = File.OpenRead(path))
            {
                pages = FileText.PagesOf(file, FileFormat.Pdf, TestContext.Current.CancellationToken);
            }

            await done.CancelAsync();
            await sampler;
            Assert.Equal(Document.MaxPages, pages.Count);
            Assert.InRange(peak - baseline, long.MinValue, Limit);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MemoryCollection
{
    public const string Name = "memory";
}