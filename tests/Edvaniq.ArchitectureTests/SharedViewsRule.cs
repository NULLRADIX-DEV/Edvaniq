using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Edvaniq.ArchitectureTests;

// The rule in docs/decisions/android-app.md: pages come into being once, in Client.UI. The web client and the app only
// host them, and the shared projects know neither host, so the same views run in the browser and on the phone.
internal static partial class SharedViewsRule
{
    private const string Shared = "src/Clients/Edvaniq.Client.UI/";

    // The web host's own page for a failure of the host itself, rendered on the server. No view of the app.
    private static readonly string[] PageExceptions = ["src/Clients/Edvaniq.Web/Components/Pages/Error.razor"];

    // Hosts that only wire up the shared views.
    private static readonly string[] Hosts = ["src/Clients/Edvaniq.Web.Client/", "src/Clients/Edvaniq.App/"];

    private static readonly string[] SharedProjects =
    [
        "src/Clients/Edvaniq.Client.UI/Edvaniq.Client.UI.csproj",
        "src/Clients/Edvaniq.Client.Core/Edvaniq.Client.Core.csproj",
        "src/Clients/Edvaniq.Client.DesignSystem/Edvaniq.Client.DesignSystem.csproj",
    ];

    private static readonly string[] ForbiddenPackages = ["Microsoft.AspNetCore.Components.WebAssembly", "Microsoft.Maui"];

    private static readonly string[] ForbiddenProjects = ["Edvaniq.Web.Client", "Edvaniq.App", "Edvaniq.Web"];

    public static IEnumerable<string> Violations(string root)
    {
        var components = Directory.EnumerateFiles(Path.Combine(root, "src", "Clients"), "*.razor", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(path => CheckComponent(path, File.ReadAllLines(Path.Combine(root, path))));

        var projects = SharedProjects
            .SelectMany(path => CheckProject(path, XDocument.Load(Path.Combine(root, path))));

        return components.Concat(projects);
    }

    public static IEnumerable<string> CheckComponent(string path, string[] lines)
    {
        if (Hosts.Any(host => path.StartsWith(host, StringComparison.Ordinal)) && Path.GetFileName(path) != "_Imports.razor")
        {
            yield return $"{path} is a component in a host, it belongs in Client.UI";
        }
        else if (!path.StartsWith(Shared, StringComparison.Ordinal) && !PageExceptions.Contains(path)
            && lines.Any(line => Page().IsMatch(line)))
        {
            yield return $"{path} is a page outside Client.UI";
        }
    }

    public static IEnumerable<string> CheckProject(string path, XDocument project)
    {
        foreach (var package in project.Descendants("PackageReference").Select(item => (string?)item.Attribute("Include") ?? ""))
        {
            if (ForbiddenPackages.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"{path} references the package {package} of one host";
            }
        }

        foreach (var reference in project.Descendants("ProjectReference").Select(item => (string?)item.Attribute("Include") ?? ""))
        {
            var name = Path.GetFileNameWithoutExtension(reference.Replace('\\', '/'));
            if (ForbiddenProjects.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                yield return $"{path} references the host {name}";
            }
        }
    }

    [GeneratedRegex(@"^\s*@page\b")]
    private static partial Regex Page();
}