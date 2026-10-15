using System.Text.Json;

namespace Edvaniq.ArchitectureTests;

// obj/project.assets.json of a project, which the restore writes: every package and project it depends on, also through
// others, with the frameworks each of them needs, and the frameworks the project references itself. The web SDK shows
// up there as the framework Microsoft.AspNetCore.App.
internal sealed record ProjectAssets(IReadOnlyList<ProjectAssets.Library> Libraries, IReadOnlyList<string> Frameworks)
{
    public sealed record Library(string Id, IReadOnlyList<string> Frameworks);

    // Null if the project is not restored.
    public static ProjectAssets? Read(ServiceProject project)
    {
        var file = FileOf(project);
        if (!File.Exists(file))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var root = document.RootElement;
        var libraries = root.GetProperty("targets").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Select(library => new Library(library.Name.Split('/')[0], Names(library.Value, "frameworkReferences")))
            .ToList();
        var frameworks = root.GetProperty("project").GetProperty("frameworks").EnumerateObject()
            .SelectMany(framework => Names(framework.Value, "frameworkReferences"))
            .Distinct()
            .ToList();
        return new ProjectAssets(libraries, frameworks);
    }

    public static string NotRestored(ServiceProject project) =>
        $"{project.Name} is not restored, {FileOf(project)} is missing. Run dotnet restore first.";

    private static string FileOf(ServiceProject project) =>
        Path.Combine(Path.GetDirectoryName(project.File)!, "obj", "project.assets.json");

    // A library lists its frameworks as an array, the project as an object with one property per framework.
    private static List<string> Names(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) ? []
        : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString()!).ToList()
        : value.EnumerateObject().Select(item => item.Name).ToList();
}