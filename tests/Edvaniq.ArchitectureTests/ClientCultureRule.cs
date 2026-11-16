using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// The browser app only has the culture data of the browser's language, a German culture there may give English names.
// So nothing under src/Clients asks for a German culture or names months or weekdays by a format, GermanDates does that.
internal static partial class ClientCultureRule
{
    private static readonly string[] Extensions = [".razor", ".cs", ".cshtml"];

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
            foreach (var found in GermanCulture().Matches(lines[index]).Select(match => match.Value))
            {
                yield return $"{path}:{index + 1} asks for {found}";
            }

            foreach (var found in Literal().Matches(lines[index]).SelectMany(literal => NameFormat().Matches(literal.Value)).Select(match => match.Value))
            {
                yield return $"{path}:{index + 1} formats {found}";
            }
        }
    }

    [GeneratedRegex(@"(GetCultureInfo|CultureInfo|CreateSpecificCulture|GetCultureInfoByIetfLanguageTag)\(\s*""de\b[^""]*""")]
    private static partial Regex GermanCulture();

    // A string literal on one line, escaped quotes included.
    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
    private static partial Regex Literal();

    // Month and weekday names in a format string: "MMMM", "d. MMM", {day:dddd}.
    [GeneratedRegex(@"(?<!M)MMM+(?!M)|(?<!d)ddd+(?!d)")]
    private static partial Regex NameFormat();
}