using System.Reflection;
using System.Text.RegularExpressions;

using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.ArchitectureTests;

// docs/architecture.md, rule 6: the topic state is LearningEngine's (#367). Planning and the clients take it over and
// never work it out, so a changed threshold only ever changes TopicStates. Nothing under src outside LearningEngine
// holds a threshold, found two ways:
// - by name: no identifier of TopicStates (the type and its public constants, read from the type itself, so a new
//   threshold is covered without touching this rule).
// - by shape: no ordering comparison of anything named mastery (<, <=, >, >=, a relational pattern, a switch). Working
//   with the value stays allowed (100 - mastery, Math.Clamp, ==), only "is it above or below" makes a threshold.
// The comparison against a number is found with or without spaces. Against a name it needs the spaces dotnet format
// puts around a binary operator (EnforceCodeStyleInBuild breaks the build otherwise): written without them, a < or >
// next to a name can't be told apart from a generic like Dictionary<Guid, TopicMastery>.
internal static partial class TopicStateRule
{
    private static readonly string[] Extensions = [".cs", ".razor"];

    public static IReadOnlyList<string> ThresholdNames { get; } =
    [
        nameof(TopicStates),
        .. typeof(TopicStates).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral || field.IsInitOnly)
            .Select(field => field.Name)
            .Order(StringComparer.Ordinal),
    ];

    public static IEnumerable<string> Violations(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
            .Where(path => !path.StartsWith("src/Services/LearningEngine/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .SelectMany(path => Check(path, File.ReadAllLines(Path.Combine(root, path))));

    public static IEnumerable<string> Check(string path, string[] lines)
    {
        var names = new Regex($@"\b({string.Join('|', ThresholdNames)})\b");
        for (var index = 0; index < lines.Length; index++)
        {
            foreach (var name in names.Matches(lines[index]).Select(match => match.Value).Distinct())
            {
                yield return $"{path}:{index + 1} names {name}";
            }

            if (MasteryComparison().IsMatch(lines[index]))
            {
                yield return $"{path}:{index + 1} compares a mastery";
            }
        }
    }

    // M is anything named mastery, possibly a member path (topic.Mastery, MasteryOf(id)); N a number.
    // M < N and N < M with or without spaces, M < name and name < M with spaces, <= and >= always (never generics),
    // M is < N, M is not > N, M is (< 50 or ...), { Mastery: < 50 }, M switch.
    [GeneratedRegex(
        """
        (?ix)
          [\w.]*mastery[\w.]*(\([^()]*\))?\s*(<|>)=?\s*-?\d
        | -?\d\s*(<|>)=?\s*[\w.]*mastery
        | [\w.]*mastery[\w.]*(\([^()]*\))?\s+(<|>)=?\s+[\w.(]
        | [\w.)]\s+(<|>)=?\s+[\w.]*mastery
        | [\w.]*mastery[\w.]*\s*(<=|>=)
        | (<=|>=)\s*[\w.]*mastery
        | mastery\w*(\([^()]*\))?\s+is\s+(not\s+)?\(?\s*(<|>)
        | mastery\w*\s*:\s*(not\s+)?(<|>)
        | mastery\w*(\([^()]*\))?\s+switch\b
        """)]
    private static partial Regex MasteryComparison();
}