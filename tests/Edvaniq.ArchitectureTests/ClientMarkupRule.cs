using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// The plain text rule in docs/architecture.md ("Clients"): the clients write text from the user or the AI only as text.
// Nothing under src/Clients turns a string into markup, neither in Razor nor in C# nor in script.
internal static partial class ClientMarkupRule
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
            foreach (var found in Forbidden().Matches(lines[index]).Select(match => match.Value))
            {
                yield return $"{path}:{index + 1} uses {found}";
            }
        }
    }

    [GeneratedRegex(@"\b(MarkupString|AddMarkupContent|Html\.Raw|innerHTML|outerHTML|insertAdjacentHTML|document\.write)\b")]
    private static partial Regex Forbidden();
}