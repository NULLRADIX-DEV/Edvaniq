using System.Text.RegularExpressions;

namespace Edvaniq.Testing;

// The shipped messages of an event from tests/EventSamples, copied next to every test that references this project.
public static partial class EventSamples
{
    public static IReadOnlyList<EventSample> For(string eventName) =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "EventSamples"), $"{eventName}.v*.json")
            .Select(path => (Path: path, Match: FileName().Match(Path.GetFileName(path))))
            .Where(file => file.Match.Success && file.Match.Groups["event"].Value == eventName)
            .Select(file => new EventSample(
                Path.GetFileName(file.Path),
                int.Parse(file.Match.Groups["version"].Value, System.Globalization.CultureInfo.InvariantCulture),
                file.Match.Groups["minimal"].Success,
                File.ReadAllText(file.Path)))
            .OrderBy(sample => sample.Version)
            .ThenBy(sample => sample.Minimal)];

    [GeneratedRegex(@"^(?<event>.+)\.v(?<version>\d+)(?<minimal>\.minimal)?\.json$")]
    private static partial Regex FileName();
}

public sealed record EventSample(string Name, int Version, bool Minimal, string Json)
{
    public override string ToString() => Name;
}