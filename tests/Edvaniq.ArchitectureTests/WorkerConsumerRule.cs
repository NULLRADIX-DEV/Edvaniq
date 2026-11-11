using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// #235: a consumer that keeps failing turns /health unhealthy, and the server alarms on an unhealthy container. A worker
// has no health check (healthcheck disable in deploy/compose.yml), so a consumer there would fail without an alarm.
// Every consumer runs in its service's Api, a worker registers no handler. One that does needs a health check first.
internal static partial class WorkerConsumerRule
{
    // The worker's own code and the AddWorker... methods of its service's Infrastructure, which only the worker calls.
    public static string SourceOf(string workerDirectory)
    {
        var own = Directory.EnumerateFiles(workerDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(File.ReadAllText);

        var service = Path.GetDirectoryName(workerDirectory)!;
        var infrastructure = Directory.EnumerateDirectories(service, "*.Infrastructure")
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => WorkerMethod().Matches(File.ReadAllText(path)).Select(match => match.Value));

        return string.Join('\n', own.Concat(infrastructure));
    }

    // The block of the service in deploy/compose.yml, up to the next service.
    public static string ComposeBlockOf(string compose, string service)
    {
        var start = compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }

        var next = ServiceStart().Match(compose, start + service.Length + 4);
        return next.Success ? compose[start..next.Index] : compose[start..];
    }

    public static string? Violation(string worker, string source, string composeBlock) =>
        source.Contains("AddIntegrationEventHandler", StringComparison.Ordinal) && HasNoHealthCheck(composeBlock)
            ? $"{worker} handles events but has no health check, a failing consumer there would raise no alarm"
            : null;

    private static bool HasNoHealthCheck(string composeBlock) =>
        composeBlock.Length == 0 || DisabledHealthCheck().IsMatch(composeBlock);

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj");

    // From the signature of a public static AddWorker... method to its closing brace at the indent of a member.
    [GeneratedRegex(@"public static \S+ AddWorker\w*.*?\n    \}", RegexOptions.Singleline)]
    private static partial Regex WorkerMethod();

    [GeneratedRegex(@"\n  [a-z0-9-]+:\n")]
    private static partial Regex ServiceStart();

    [GeneratedRegex(@"healthcheck:\s*\n\s*disable:\s*true")]
    private static partial Regex DisabledHealthCheck();
}