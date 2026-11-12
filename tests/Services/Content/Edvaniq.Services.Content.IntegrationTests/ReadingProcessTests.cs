using System.Diagnostics;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Files;

using Microsoft.Extensions.Options;

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Edvaniq.Services.Content.IntegrationTests;

// #318: a file is read by the worker started once more as a process of its own, with a capped heap. Whatever the file
// does, it ends that process and never the worker, and no reading process outlives its read. No database needed.
public sealed class ReadingProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "edvaniq-reading", Guid.NewGuid().ToString("N"));

    private readonly FileStore store;

    public ReadingProcessTests() => store = new FileStore(Options.Create(new FileStorageOptions { Directory = root }));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OrdinaryFile_ComesBackPageByPage()
    {
        var (pdf, pages) = TestPdfs.OneColumn(["Licht spaltet Wasser in der Lichtreaktion der Photosynthese."]);

        var read = await ReadAsync(pdf, FileFormat.Pdf);

        Assert.Equal(pages.Select(TestPdfs.Words), read.Pages!.Select(TestPdfs.Words));
    }

    [Fact]
    public async Task ProtectedAndPackedFiles_ComeBackWithTheirReason()
    {
        Assert.Equal(FileCheck.PdfProtected, (await ReadAsync(ProblemFiles.EncryptedPdf(), FileFormat.Pdf)).Reason);
        Assert.Equal(FileCheck.PackedTooTightly, (await ReadAsync(ProblemFiles.PackedPdf(512L * 1024 * 1024), FileFormat.Pdf)).Reason);
        Assert.Equal(Document.FileUnreadable, (await ReadAsync(ProblemFiles.GarbagePdf(), FileFormat.Pdf)).Reason);
    }

    // The text of a file can look like the answer of the reading process, it stays text.
    [Fact]
    public async Task TextThatLooksLikeAnAnswer_StaysText()
    {
        var read = await ReadAsync("EDVQ\u0001\u0007 und weiter im Text."u8.ToArray(), FileFormat.Text);

        Assert.Equal(["EDVQ\u0001\u0007 und weiter im Text."], read.Pages);
    }

    [Fact]
    public async Task ReadingThatTakesTooLong_IsKilled()
    {
        var reader = ReadingProcess.Reader(store, killAfter: TimeSpan.FromMilliseconds(50));

        var read = await reader.ReadAsync(await StoreAsync(TestPdfs.Large(Document.MaxPages, 3, 9)), FileFormat.Pdf, Cancel);

        Assert.Equal(FileCheck.TakesTooLong, read.Reason);
        Assert.True(IsGone(reader.LastProcessId!.Value), "The reading process outlived its read.");
    }

    // A deploy stops the worker: the read ends as a cancellation, and the reading process with it.
    [Fact]
    public async Task Shutdown_EndsTheReadingProcess()
    {
        var reader = ReadingProcess.Reader(store);
        var id = await StoreAsync(TestPdfs.Large(Document.MaxPages, 3, 9));
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        stopping.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(id, FileFormat.Pdf, stopping.Token));

        Assert.True(IsGone(reader.LastProcessId!.Value), "The reading process outlived the worker's shutdown.");
    }

    // The reading process builds no host: nothing but its answer is on its output, although it gets no settings at all.
    [Fact]
    public async Task ReadingProcess_StartsNoHost_AndGetsNoSettings()
    {
        var path = store.PathOf(await StoreAsync("Ein ganz normaler Text."u8.ToArray()));
        var start = ReadingProcess.Reader(store).StartInfo(path, FileFormat.Text);

        Assert.DoesNotContain(start.Environment.Keys, key => key.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(start.Environment.Keys, key => key.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        using var output = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(output, Cancel);
        await process.WaitForExitAsync(Cancel);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(["Ein ganz normaler Text."], FileReadProtocol.Read(output.ToArray())!.Pages);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<FileReadResult> ReadAsync(byte[] content, FileFormat format) =>
        await ReadingProcess.Reader(store).ReadAsync(await StoreAsync(content), format, Cancel);

    private async Task<Guid> StoreAsync(byte[] content)
    {
        var id = Guid.NewGuid();
        await store.WriteAsync(id, new MemoryStream(content), Cancel);
        return id;
    }

    private static bool IsGone(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

}

// The memory of the worker while a file breaks its reading process. It reads the memory of the whole test process, so
// it runs alone (MemoryCollection): tests next to it would count as the worker's.
[Collection(MemoryCollection.Name)]
public sealed class ReadingProcessMemoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "edvaniq-reading", Guid.NewGuid().ToString("N"));

    private readonly FileStore store;

    public ReadingProcessMemoryTests() => store = new FileStore(Options.Create(new FileStorageOptions { Directory = root }));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // One page with a million letters: a few KB of file, far more memory than the worker has to read it. The reading
    // process runs out of its heap, the worker only hears the reason. The file is made and stored before measuring.
    [Fact]
    public async Task PageThatNeedsMoreMemoryThanAllowed_EndsTheReadingProcess_NotTheWorker()
    {
        var id = Guid.NewGuid();
        await store.WriteAsync(id, new MemoryStream(LetterPage(1_000_000)), Cancel);
        var reader = ReadingProcess.Reader(store);
        GC.Collect();
        var before = Process.GetCurrentProcess().PrivateMemorySize64;

        var read = await reader.ReadAsync(id, FileFormat.Pdf, Cancel);

        Assert.Equal(Document.FileNotProcessable, read.Reason);
        Assert.InRange(Process.GetCurrentProcess().PrivateMemorySize64 - before, long.MinValue, 64L * 1024 * 1024);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] LetterPage(int letters)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        for (var line = 0; line < letters / 2000; line++)
        {
            page.AddText(new string('a', 2000), 1, new PdfPoint(10, 800 - (line * 0.1)), font);
        }

        return builder.Build();
    }
}