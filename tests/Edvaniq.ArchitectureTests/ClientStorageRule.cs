using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// The server is the only source of the user's data (docs/architecture.md, "Clients"): nothing under src/Clients keeps
// a state of its own in the browser's or the device's storage, a page only holds what it fetched last.
internal static partial class ClientStorageRule
{
    private static readonly string[] Extensions = [".razor", ".cs", ".cshtml", ".js", ".html"];

    public static IEnumerable<string> Violations(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src", "Clients"), "*", SearchOption.AllDirectories)
            .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(path => Check(path, File.ReadAllLines(Path.Combine(root, path))));

    public static IEnumerable<string> Check(string path, string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var found in Forbidden().Matches(lines[index]).Select(match => match.Groups["store"].Value))
            {
                yield return $"{path}:{index + 1} uses {found}";
            }
        }
    }

    // Only an access counts, so a word like "caches" in a comment does not.
    [GeneratedRegex(@"\b(?<store>localStorage|sessionStorage|indexedDB|caches|Preferences|SecureStorage)\s*\.\s*[A-Za-z_]")]
    private static partial Regex Forbidden();
}