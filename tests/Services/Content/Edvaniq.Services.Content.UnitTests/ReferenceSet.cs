using System.Text.Json;

using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Content.UnitTests;

// The reference set in tests/ReferenceSet: documents written for testing the analysis, each with the topics we expect
// from it. The rule for what counts as a hit is in tests/ReferenceSet/README.md.
internal sealed record ReferenceSet(int Version, IReadOnlyList<ReferenceDocument> Documents)
{
    public static string Folder { get; } = Path.Combine(FindRoot(), "tests", "ReferenceSet");

    public static string ManifestPath { get; } = Path.Combine(Folder, "reference-set.json");

    public static ReferenceSet Load()
    {
        if (!File.Exists(ManifestPath))
        {
            throw new FileNotFoundException("The reference set has no manifest at " + ManifestPath);
        }

        return JsonSerializer.Deserialize<ReferenceSet>(File.ReadAllText(ManifestPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The manifest of the reference set is empty: " + ManifestPath);
    }

    // The same rule Knowledge merges topics by.
    public static string Normalize(string name) => TopicName.Normalize(name);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Edvaniq.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Edvaniq.slnx not found above " + AppContext.BaseDirectory);
    }
}

internal sealed record ReferenceDocument(
    string Id,
    string File,
    string? Title,
    string Subject,
    string Kind,
    bool Analyzable,
    string? Note,
    IReadOnlyList<ExpectedTopic>? Topics,
    IReadOnlyList<string>? Forbidden)
{
    public string Text => System.IO.File.ReadAllText(Path.Combine(ReferenceSet.Folder, File));

    // Every topic and subtopic, each with its path of names from the top.
    public IEnumerable<(ExpectedTopic Topic, string Path)> AllTopics() =>
        (Topics ?? []).SelectMany(topic => (topic.Subtopics ?? [])
            .Select(subtopic => (subtopic, topic.Name + " / " + subtopic.Name))
            .Prepend((topic, topic.Name)));
}

internal sealed record ExpectedTopic(
    string Name,
    IReadOnlyList<string>? Aliases,
    IReadOnlyList<string>? Anchors,
    IReadOnlyList<ExpectedTopic>? Subtopics)
{
    public IEnumerable<string> Names => (Aliases ?? []).Prepend(Name);
}