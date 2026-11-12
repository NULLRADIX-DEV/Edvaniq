using System.Diagnostics;

using Edvaniq.Services.Content.Domain;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure.Files;

public sealed class FileReadingOptions
{
    public const string Section = "FileReading";

    // The program to start, and the assembly to hand it if that program is the dotnet host. Empty means the worker's
    // own: in the image that is its apphost, run with "dotnet <worker>.dll" the host and the worker's assembly. Tests set
    // both to the worker they built.
    public string Command { get; set; } = "";

    public string Assembly { get; set; } = "";

    // The managed heap of the reading process. Together with the worker at rest it stays well inside the container.
    public int HeapLimitMegabytes { get; set; } = 80;

    // On top of FileText.TimeLimit, which the reading process keeps itself: past it the process is killed, even if it
    // hangs where nothing checks the time.
    public TimeSpan KillAfter { get; set; } = FileText.TimeLimit + TimeSpan.FromSeconds(15);
}

// Reads a stored file in a process of its own (FileReadCommand), so a file that wants more memory or time than it may
// have takes down that process and never the worker. One process per file, one file at a time. Nothing from the process
// reaches the log but its exit code and how much it wrote: its error output could quote the file.
public sealed class FileReader(FileStore files, IOptions<FileReadingOptions> options, ILogger<FileReader> logger)
{
    // Only what a .NET program needs to start. The worker's own settings and secrets stay with the worker.
    private static readonly string[] PassedOn = ["PATH", "HOME", "DOTNET_ROOT", "TMPDIR", "SystemRoot", "TEMP", "TMP"];

    // The last reading process started, for the check that none outlives its read.
    public int? LastProcessId { get; private set; }

    public async Task<FileReadResult> ReadAsync(Guid fileId, FileFormat format, CancellationToken cancellationToken)
    {
        var path = files.PathOf(fileId);
        var start = StartInfo(path, format);
        using var process = new Process { StartInfo = start };
        var watch = Stopwatch.StartNew();
        process.Start();
        LastProcessId = process.Id;
        process.StandardInput.Close();
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, FileReadProtocol.MaxLength + 1, cancellationToken);
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, cancellationToken);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(options.Value.KillAfter);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Kill(process);
                ContentLog.FileReaderKilled(logger, watch.ElapsedMilliseconds);
                return FileReadResult.Refused(DocumentFailures.TakesTooLong);
            }

            var frame = await output;
            await errors;
            ContentLog.FileReaderEnded(logger, process.ExitCode, frame.Length, watch.ElapsedMilliseconds);

            // A process that ends without an answer ran out of memory where nothing could catch it.
            return process.ExitCode == 0 && FileReadProtocol.Read(frame) is { } result
                ? result
                : FileReadResult.Refused(process.ExitCode == 0 ? DocumentFailures.FileUnreadable : DocumentFailures.FileNotProcessable);
        }
        finally
        {
            // A shutdown or anything that failed above: no reading process outlives its read.
            Kill(process);
        }
    }

    // How the reading process starts, public for the check that it gets nothing of the worker's settings.
    public ProcessStartInfo StartInfo(string path, FileFormat format)
    {
        var (command, assembly) = Command();
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (assembly is not null)
        {
            start.ArgumentList.Add(assembly);
        }

        start.ArgumentList.Add(FileReadCommand.Name);
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(format.ToString());

        start.Environment.Clear();
        foreach (var name in PassedOn)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                start.Environment[name] = value;
            }
        }

        start.Environment["DOTNET_GCHeapHardLimit"] = ((long)options.Value.HeapLimitMegabytes * 1024 * 1024).ToString("X", System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["DOTNET_gcServer"] = "0";
        start.Environment["DOTNET_TieredPGO"] = "0";
        return start;
    }

    private (string Command, string? Assembly) Command()
    {
        var configured = options.Value;
        if (configured.Command.Length > 0)
        {
            return (configured.Command, configured.Assembly.Length > 0 ? configured.Assembly : null);
        }

        var own = Environment.ProcessPath ?? throw new InvalidOperationException("The worker doesn't know its own program.");
        var isHost = Path.GetFileNameWithoutExtension(own).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        return (own, isHost ? System.Reflection.Assembly.GetEntryAssembly()?.Location : null);
    }

    // Takes at most limit bytes, a longer answer is no valid one anyway and is cut there.
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                await stream.CopyToAsync(Stream.Null, cancellationToken);
                return [];
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException)
        {
            // It ended in between.
        }
    }
}