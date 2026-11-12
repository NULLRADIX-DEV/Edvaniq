using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Files;

using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace Edvaniq.Services.Content.IntegrationTests;

// #318: broken and hostile files end with a reason the user understands, never with a crash or a worker out of memory.
// No database needed.
public sealed class ProblemFileTests
{
    private const long StreamLimit = 16L * 1024 * 1024;

    private const long Budget = 64L * 1024 * 1024;

    // Packed to about a hundredth, unpacked far beyond what a stream may take. Memory stays low: the stream is counted,
    // not kept.
    [Fact]
    public void PackedTooTightly_IsRefused_WithoutUnpackingIt()
    {
        var bomb = ProblemFiles.PackedPdf(512L * 1024 * 1024);
        var before = GC.GetTotalAllocatedBytes(precise: true);

        var refused = Assert.Throws<FileRefusedException>(() => Read(bomb));

        Assert.Equal(FileCheck.PackedTooTightly, refused.Reason);
        Assert.InRange(GC.GetTotalAllocatedBytes(precise: true) - before, 0, 64L * 1024 * 1024);
    }

    [Fact]
    public void PackedWithinTheLimit_IsRead()
    {
        var pdf = ProblemFiles.PackedPdf(1024 * 1024);

        Assert.Equal(1, Read(pdf));
    }

    // Many streams that each fit add up, the budget stops them together.
    [Fact]
    public void Budget_CountsEveryStreamOfTheFile()
    {
        var pdf = ProblemFiles.PackedPdf(8L * 1024 * 1024);

        var refused = Assert.Throws<FileRefusedException>(() => Read(pdf, budget: 4L * 1024 * 1024));

        Assert.Equal(FileCheck.PackedTooTightly, refused.Reason);
    }

    [Fact]
    public void Encrypted_IsKnownAsSuch() =>
        Assert.Throws<PdfDocumentEncryptedException>(() => Read(ProblemFiles.EncryptedPdf()));

    [Fact]
    public void Truncated_AndGarbage_FailWithoutHanging()
    {
        foreach (var pdf in new[] { ProblemFiles.TruncatedPdf(), ProblemFiles.GarbagePdf() })
        {
            var started = DateTime.UtcNow;
            try
            {
                Read(pdf);
            }
            catch (Exception exception) when (exception is not FileRefusedException)
            {
            }

            Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }
    }

    // Opens the PDF the way the worker does and reads every page, returns how many.
    private static int Read(byte[] pdf, long budget = Budget)
    {
        using var document = PdfDocument.Open(pdf, new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
            FilterProvider = new BoundedPdfFilters(StreamLimit, budget, TestContext.Current.CancellationToken),
        });
        var pages = 0;
        foreach (var page in document.GetPages())
        {
            _ = page.Letters.Count;
            pages++;
        }

        return pages;
    }
}