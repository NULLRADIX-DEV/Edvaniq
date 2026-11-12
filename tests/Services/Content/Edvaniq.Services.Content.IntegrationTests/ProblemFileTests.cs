using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Files;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Edvaniq.Services.Content.IntegrationTests;

// #318: broken and hostile files end with a reason the user understands, never with a crash or a worker out of memory.
// FileText the way the worker calls it, no database needed. The way through the worker is in FileTextExtractorTests.
public sealed class ProblemFileTests
{
    // Packed to about a thousandth, unpacked far beyond what a stream may take. Memory stays low: the stream is counted,
    // not kept.
    [Fact]
    public void PackedTooTightly_IsRefused_WithoutUnpackingIt()
    {
        var bomb = ProblemFiles.PackedPdf(512L * 1024 * 1024);
        var before = GC.GetTotalAllocatedBytes(precise: true);

        Assert.Equal(FileCheck.PackedTooTightly, Refusal(bomb));
        Assert.InRange(GC.GetTotalAllocatedBytes(precise: true) - before, 0, 64L * 1024 * 1024);
    }

    [Fact]
    public void PackedWithinTheLimit_IsRead() =>
        Assert.Single(Pages(ProblemFiles.PackedPdf(1024 * 1024)));

    // Many streams that each fit add up, the budget stops them together.
    [Fact]
    public void Budget_CountsEveryStreamOfTheFile()
    {
        var pdf = ProblemFiles.PackedPdf(8L * 1024 * 1024);
        var filters = new BoundedPdfFilters(FileText.PdfStreamLimit, 4L * 1024 * 1024, TestContext.Current.CancellationToken);

        var refused = Assert.Throws<FileRefusedException>(() =>
        {
            using var document = PdfDocument.Open(pdf, new ParsingOptions { FilterProvider = filters });
            _ = document.GetPage(1).Letters.Count;
        });

        Assert.Equal(FileCheck.PackedTooTightly, refused.Reason);
    }

    [Fact]
    public void Encrypted_IsRefusedAsProtected() =>
        Assert.Equal(FileCheck.PdfProtected, Refusal(ProblemFiles.EncryptedPdf()));

    // Broken files are no refusal with a reason of their own, the worker fails them as unreadable. They fail at once.
    [Fact]
    public void Truncated_AndGarbage_FailWithoutHanging()
    {
        foreach (var pdf in new[] { ProblemFiles.TruncatedPdf(), ProblemFiles.GarbagePdf() })
        {
            var started = TimeProvider.System.GetTimestamp();

            Assert.ThrowsAny<Exception>(() => Pages(pdf));

            Assert.InRange(TimeProvider.System.GetElapsedTime(started), TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }
    }

    // A page with more letters than a document may hold is refused before its layout, which costs far more.
    [Fact]
    public void PageWithTooManyLetters_IsRefused()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        for (var line = 0; line < (Document.TextMaxLength / 2000) + 1; line++)
        {
            page.AddText(new string('a', 2000), 1, new PdfPoint(10, 800 - (line * 0.5)), font);
        }

        Assert.Equal(Document.FileTextTooLong, Refusal(builder.Build()));
    }

    [Fact]
    public void FileThatTakesTooLong_IsRefused() =>
        Assert.Equal(
            FileCheck.TakesTooLong,
            Assert.Throws<FileRefusedException>(() => FileText.PagesOf(
                new MemoryStream(TestPdfs.Empty(3)), FileFormat.Pdf, TimeSpan.Zero, TestContext.Current.CancellationToken)).Reason);

    // A shutdown is no file that takes too long, it stays a cancellation.
    [Fact]
    public void Shutdown_StaysACancellation()
    {
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => FileText.PagesOf(new MemoryStream(TestPdfs.Empty(3)), FileFormat.Pdf, stopping.Token));
    }

    private static IReadOnlyList<string> Pages(byte[] pdf) =>
        FileText.PagesOf(new MemoryStream(pdf), FileFormat.Pdf, TestContext.Current.CancellationToken);

    private static string Refusal(byte[] pdf) => Assert.Throws<FileRefusedException>(() => Pages(pdf)).Reason;
}