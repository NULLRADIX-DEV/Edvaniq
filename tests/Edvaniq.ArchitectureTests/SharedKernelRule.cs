using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Edvaniq.ArchitectureTests;

// Edvaniq.SharedKernel holds only domain rules the spec fixes for all services alike, such as the study day. So it
// depends on nothing, neither a project nor a package, and only the Domain of a service and the ServiceDefaults (for
// the check at startup) reference it.
internal static partial class SharedKernelRule
{
    public const string Name = "Edvaniq.SharedKernel";

    public static IEnumerable<string> Violations(string root)
    {
        var projects = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"))
            .Select(file => (Name: Path.GetFileNameWithoutExtension(file), Project: XDocument.Load(file)))
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var (name, project) in projects)
        {
            if (name == Name)
            {
                foreach (var reference in project.Descendants()
                    .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference" or "FrameworkReference"))
                {
                    var include = Path.GetFileName((reference.Attribute("Include")?.Value ?? "").Replace('\\', '/'));
                    yield return $"{Name} has the {reference.Name.LocalName} {(include.EndsWith(".csproj", StringComparison.Ordinal) ? include[..^".csproj".Length] : include)}";
                }
            }
            else if (!MayReference().IsMatch(name) && project.Descendants("ProjectReference")
                .Any(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value.Replace('\\', '/')) == Name))
            {
                yield return $"{name} references {Name}, only a Domain and the ServiceDefaults may";
            }
        }
    }

    [GeneratedRegex(@"^(Edvaniq\.Services\.[A-Za-z0-9]+\.Domain|Edvaniq\.ServiceDefaults)$")]
    private static partial Regex MayReference();
}