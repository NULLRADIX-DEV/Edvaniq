using System.Reflection;
using System.Xml.Linq;

namespace Edvaniq.ArchitectureTests;

// A project of a service: src/Services/<Service>/<Name>/<Name>.csproj with the names of the projects it references
// directly. Layer is what follows Edvaniq.Services.<Service>. in the name, or null if the name does not start that way.
internal sealed record ServiceProject(string Service, string? Layer, string Name, string File, IReadOnlyList<string> References)
{
    public const string Prefix = "Edvaniq.Services.";

    public static IReadOnlyList<ServiceProject> ReadAll(string root) =>
        Directory.EnumerateDirectories(Path.Combine(root, "src", "Services"))
            .SelectMany(service => Directory.EnumerateDirectories(service)
                .SelectMany(project => Directory.EnumerateFiles(project, "*.csproj"))
                .Select(file => Read(Path.GetFileName(service), file)))
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();

    // The built project from the output of this test project, or null if the tests don't reference it.
    public Assembly? Load()
    {
        try
        {
            return Assembly.Load(new AssemblyName(Name));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    // Service and layer of a referenced project, or null if it is no project of a service.
    public static (string Service, string Layer)? Parse(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = name[Prefix.Length..].Split('.', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : null;
    }

    private static ServiceProject Read(string service, string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var prefix = $"{Prefix}{service}.";
        var layer = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : null;
        var references = XDocument.Load(file).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
        return new ServiceProject(service, layer, name, file, references);
    }
}