using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// Rule 4 in docs/architecture.md: the address of another service never sits in a service, it comes from outside. The
// code (*.cs) and the appsettings*.json under src/Services hold no URL, no IPv4 address and no localhost. Allowed are a
// name of the service discovery (https+http://<resource>), which Aspire or deploy/compose.yml resolve, localhost in a
// design-time DbContext factory, which only dotnet ef uses, comment lines and the build output in bin and obj.
internal static partial class AddressRule
{
    public static IEnumerable<string> Violations(string root)
    {
        return Directory.EnumerateFiles(Path.Combine(root, "src", "Services"), "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(IsChecked)
            .Order(StringComparer.Ordinal)
            .SelectMany(path => Check(root, path));
    }

    private static bool IsChecked(string path)
    {
        var parts = path.Split('/');
        var name = parts[^1];
        return !parts.Any(part => part is "bin" or "obj")
            && (name.EndsWith(".cs", StringComparison.Ordinal)
                || (name.StartsWith("appsettings", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Check(string root, string path)
    {
        var lines = File.ReadAllLines(Path.Combine(root, path));
        var designTime = lines.Any(line => line.Contains("IDesignTimeDbContextFactory<", StringComparison.Ordinal));
        for (var index = 0; index < lines.Length; index++)
        {
            if (IsComment(lines[index]))
            {
                continue;
            }

            foreach (var address in Address().Matches(lines[index]).Select(match => match.Value))
            {
                if (!Discovery().IsMatch(address) && !(designTime && address.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return $"{path}:{index + 1} holds the address {address}";
                }
            }
        }
    }

    private static bool IsComment(string line)
    {
        var start = line.TrimStart();
        return start.StartsWith("//", StringComparison.Ordinal) || start.StartsWith("/*", StringComparison.Ordinal) || start.StartsWith('*');
    }

    [GeneratedRegex(@"\b[a-z][a-z0-9.+-]*://[^\s""'`]*|\b\d{1,3}(\.\d{1,3}){3}\b|\blocalhost\b", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    // The host of a discovery name is a resource name, never localhost and never with a dot, so never an IP.
    [GeneratedRegex(@"^https?\+https?://(?!localhost\b)[a-z][a-z0-9-]*(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Discovery();
}