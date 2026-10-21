using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading.Channels;

namespace Edvaniq.Spikes.Broker;

// The consumer in a process of its own, like a second service. It reports on stdout what it did.
public sealed class ConsumerProcess : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly string ConsumerPath = typeof(ConsumerProcess).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == nameof(ConsumerPath))
        .Value!;

    private readonly Process process;
    private readonly Channel<string> lines = Channel.CreateUnbounded<string>();
    private readonly StringBuilder errors = new();

    private ConsumerProcess(IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo("dotnet", [ConsumerPath])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is null)
            {
                lines.Writer.TryComplete();
            }
            else
            {
                lines.Writer.TryWrite(line.Data);
            }
        };
        process.ErrorDataReceived += (_, line) =>
        {
            lock (errors)
            {
                errors.AppendLine(line.Data);
            }
        };
    }

    public static async Task<ConsumerProcess> StartAsync(IReadOnlyDictionary<string, string> environment)
    {
        var consumer = new ConsumerProcess(environment);
        consumer.process.Start();
        consumer.process.BeginOutputReadLine();
        consumer.process.BeginErrorReadLine();
        await consumer.WaitForLineAsync("ready");
        return consumer;
    }

    public async Task WaitForLineAsync(string expected)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            while (await lines.Reader.ReadAsync(timeout.Token) != expected)
            {
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException)
        {
            lock (errors)
            {
                throw new InvalidOperationException($"Consumer never wrote \"{expected}\".{Environment.NewLine}{errors}", exception);
            }
        }
    }

    public async Task WaitForExitAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await process.WaitForExitAsync(timeout.Token);
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.Dispose();
    }
}