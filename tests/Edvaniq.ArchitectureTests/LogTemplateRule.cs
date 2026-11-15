using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// No log line names a user or carries what a user wrote (#417): the templates of [LoggerMessage] under src/ have no
// placeholder for a user, an e-mail address, a text, an answer, a prompt or a name of a topic or document. Ids, numbers
// and error types are fine. {Name} and {Title} are left out on purpose, a consumer or handler has a name too.
internal static partial class LogTemplateRule
{
    public static readonly IReadOnlySet<string> Forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "UserId", "OwnerId", "User", "Owner", "Email", "Text", "Answer", "Prompt", "TopicName", "DocumentTitle",
    };

    public static IEnumerable<string> Violations(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(path => Check(path, File.ReadAllText(Path.Combine(root, path))));

    public static IEnumerable<string> Check(string path, string source)
    {
        foreach (Match attribute in Attribute().Matches(source))
        {
            if (Template().Match(attribute.Value) is not { Success: true } template)
            {
                continue;
            }

            var lineStart = source.LastIndexOf('\n', Math.Max(attribute.Index - 1, 0)) + 1;
            if (source[lineStart..attribute.Index].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var line = source[..attribute.Index].Count(c => c == '\n') + 1;
            foreach (var name in Placeholder().Matches(template.Groups["text"].Value).Select(match => match.Groups["name"].Value))
            {
                if (Forbidden.Contains(name))
                {
                    yield return $"{path}:{line} logs {{{name}}}";
                }
            }
        }
    }

    [GeneratedRegex(@"\[LoggerMessage\((?:[^()""]|""(?:[^""\\]|\\.)*"")*\)\]", RegexOptions.Singleline)]
    private static partial Regex Attribute();

    [GeneratedRegex(@"Message\s*=\s*""(?<text>(?:[^""\\]|\\.)*)""")]
    private static partial Regex Template();

    [GeneratedRegex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)[^{}]*\}")]
    private static partial Regex Placeholder();
}