using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.ServiceDefaults;

// Logs once at startup which commit the process was built from, so the log of every process (workers included) shows
// the release it runs. The SDK appends the commit to the informational version ("1.0.0+<sha>") when it builds from git.
internal sealed partial class CommitLogger(ILogger<CommitLogger> logger) : IHostedService
{
    public const string Unknown = "unknown";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogCommit(logger, CommitOf(Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static string CommitOf(string? informationalVersion)
    {
        var match = informationalVersion is null ? null : Sha().Match(informationalVersion);
        return match is { Success: true } ? match.Groups[1].Value : Unknown;
    }

    [GeneratedRegex(@"\+([0-9a-f]{40})$")]
    private static partial Regex Sha();

    [LoggerMessage(Level = LogLevel.Information, Message = "Commit: {Commit}")]
    private static partial void LogCommit(ILogger logger, string commit);
}