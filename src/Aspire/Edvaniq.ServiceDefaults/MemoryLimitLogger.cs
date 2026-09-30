using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.ServiceDefaults;

// Logs once at startup how much memory the GC may use. In a container with a memory limit .NET caps the heap at 75 %
// of that limit (192 MiB for 256m), so the log shows that the process itself knows the limit, not only Docker.
internal sealed partial class MemoryLimitLogger(ILogger<MemoryLimitLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogMemoryLimit(logger, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "GC memory limit: {MemoryLimitMiB} MiB")]
    private static partial void LogMemoryLimit(ILogger logger, long memoryLimitMiB);
}
