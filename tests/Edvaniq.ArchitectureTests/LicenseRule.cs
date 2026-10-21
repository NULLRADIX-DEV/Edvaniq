using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Edvaniq.ArchitectureTests;

// docs/licenses.md: every third-party component the repository uses stands in the license list, with the license it
// really has. Read are the direct packages (Directory.Packages.props, the SDKs under msbuild-sdks in global.json and
// those with a version in a project file),
// every package in obj/project.assets.json of each project in Edvaniq.slnx with the license expression of its nuspec,
// the tools in dotnet-tools.json, the actions in .github/workflows and the images in deploy, in the workflows and in the
// tests, except our own. Base images, the images Aspire pulls itself and programs a workflow downloads are not found
// here and stay a matter of the review.
internal static partial class LicenseRule
{
    private const string OwnImages = "ghcr.io/nullradix-dev/";

    // License is the SPDX expression in the nuspec, or null if the nuspec names its license only as a file or a link.
    public sealed record Package(string Id, string Version, string Project, string? License, bool HasNuspec);

    public sealed record Components(
        IReadOnlySet<string> Direct,
        IReadOnlyList<Package> Packages,
        IReadOnlySet<string> Tools,
        IReadOnlySet<string> Actions,
        IReadOnlySet<string> Images);

    public static IEnumerable<string> Violations(string root)
    {
        var list = LicenseList.Read(File.ReadAllText(Path.Combine(root, "docs", "licenses.md")));
        return NotRestored(root).Concat(Violations(list, Read(root)));
    }

    public static IEnumerable<string> Violations(LicenseList list, Components used)
    {
        var listed = list.Rows.ToLookup(row => row.Table);
        var packages = list.Rows.Where(row => row.Table is LicenseList.Packages or LicenseList.IndirectPackages)
            .ToDictionary(row => row.Component, StringComparer.OrdinalIgnoreCase);
        var direct = listed[LicenseList.Packages].Select(row => row.Component).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in used.Direct.Where(id => !direct.Contains(id)).Order(StringComparer.Ordinal))
        {
            yield return $"{id} is a direct package but not listed under \"{LicenseList.Packages}\"";
        }

        foreach (var row in listed[LicenseList.Packages].Where(row => !used.Direct.Contains(row.Component)))
        {
            yield return $"{row.Component} is listed under \"{LicenseList.Packages}\" but no direct package";
        }

        foreach (var row in list.Rows.Where(row => !row.IsApproved && !list.IsAllowed(row.License)))
        {
            yield return $"{row.Component} is listed with the license {row.License}, which is not allowed and not approved";
        }

        var distinct = used.Packages
            .GroupBy(package => (package.Id, package.Version))
            .Select(group => group.OrderBy(package => package.Project, StringComparer.Ordinal).First())
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Version, StringComparer.Ordinal);
        foreach (var package in distinct)
        {
            var name = $"{package.Id} {package.Version} (in {package.Project})";
            if (!package.HasNuspec)
            {
                yield return $"{name} has no nuspec in the package folders. Run dotnet restore first.";
            }
            else if (packages.TryGetValue(package.Id, out var row))
            {
                if (package.License is not null && package.License != row.License)
                {
                    yield return $"{name} has the license {package.License} in its nuspec, but the list says {row.License}";
                }
            }
            else if (!used.Direct.Contains(package.Id)) // an unlisted direct package is reported above
            {
                if (package.License is null)
                {
                    yield return $"{name} names its license only as a file or link and is not listed under \"{LicenseList.IndirectPackages}\"";
                }
                else if (!list.IsAllowed(package.License))
                {
                    yield return $"{name} has the license {package.License}, which is not allowed, and is not listed";
                }
            }
        }

        foreach (var (table, components) in new[]
                 {
                     (LicenseList.Tools, used.Tools), (LicenseList.Actions, used.Actions), (LicenseList.Images, used.Images),
                 })
        {
            var names = listed[table].Select(row => row.Component).ToHashSet(StringComparer.Ordinal);
            foreach (var component in components.Where(component => !names.Contains(component)).Order(StringComparer.Ordinal))
            {
                yield return $"{component} is used but not listed under \"{table}\"";
            }
        }
    }

    public static Components Read(string root)
    {
        var projects = Projects(root).ToList();
        var direct = XDocument.Load(Path.Combine(root, "Directory.Packages.props")).Descendants("PackageVersion")
            .Select(package => package.Attribute("Include")!.Value)
            .Concat(projects.Select(project => XDocument.Load(project).Root!.Attribute("Sdk")?.Value)
                .Where(sdk => sdk is not null && sdk.Contains('/'))
                .Select(sdk => sdk!.Split('/')[0]))
            .Concat(MsBuildSdks(root))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var licenses = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var packages = projects.Select(AssetsOf).Where(File.Exists).SelectMany(assets => PackagesIn(assets, licenses)).ToList();

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dotnet-tools.json")));
        var tools = manifest.RootElement.GetProperty("tools").EnumerateObject().Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        var workflows = Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml").Select(File.ReadAllText).ToList();
        var actions = workflows.SelectMany(workflow => Action().Matches(workflow)).Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var images = Directory.EnumerateFiles(Path.Combine(root, "deploy"), "*.yml")
            .SelectMany(file => ComposeImage().Matches(File.ReadAllText(file)))
            .Concat(workflows.SelectMany(workflow => WorkflowImage().Matches(workflow)))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"))
                .SelectMany(file => TestImage().Matches(File.ReadAllText(file))))
            .Select(match => match.Groups[1].Value)
            .Where(image => !image.StartsWith(OwnImages, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        return new Components(direct, packages, tools, actions, images);
    }

    public static IEnumerable<string> NotRestored(string root) =>
        Projects(root).Where(project => !File.Exists(AssetsOf(project)))
            .Select(project => $"{Path.GetFileNameWithoutExtension(project)} is not restored, "
                + $"{Path.GetRelativePath(root, AssetsOf(project))} is missing. Run dotnet restore first.");

    // The SDKs whose version global.json sets under msbuild-sdks, empty without global.json or the section.
    private static List<string> MsBuildSdks(string root)
    {
        var file = Path.Combine(root, "global.json");
        if (!File.Exists(file))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        return document.RootElement.TryGetProperty("msbuild-sdks", out var sdks)
            ? sdks.EnumerateObject().Select(sdk => sdk.Name).ToList()
            : [];
    }

    // The project files in Edvaniq.slnx.
    private static IEnumerable<string> Projects(string root) =>
        XDocument.Load(Path.Combine(root, "Edvaniq.slnx")).Descendants("Project")
            .Select(project => Path.Combine(root, project.Attribute("Path")!.Value.Replace('\\', '/')));

    private static string AssetsOf(string project) =>
        Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");

    // The packages in one project.assets.json. Licenses holds the license of each nuspec read so far, because most
    // projects share most of their packages.
    private static List<Package> PackagesIn(string assets, Dictionary<string, string?> licenses)
    {
        var project = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(assets)))!;
        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var folders = document.RootElement.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToList();
        var packages = new List<Package>();
        foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package")
            {
                continue;
            }

            var name = library.Name.Split('/');
            var path = library.Value.GetProperty("path").GetString()!;
            var nuspec = library.Value.GetProperty("files").EnumerateArray().Select(file => file.GetString()!)
                .FirstOrDefault(file => file.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            var file = nuspec is null ? null : folders.Select(folder => Path.Combine(folder, path, nuspec)).FirstOrDefault(File.Exists);
            if (file is not null && !licenses.ContainsKey(file))
            {
                licenses[file] = LicenseOf(file);
            }

            packages.Add(new Package(name[0], name[1], project, file is null ? null : licenses[file], file is not null));
        }

        return packages;
    }

    private static string? LicenseOf(string nuspec) =>
        XDocument.Load(nuspec).Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "license" && (string?)element.Attribute("type") == "expression")
            ?.Value.Trim();

    [GeneratedRegex(@"uses:\s*([^@\s]+)@")]
    private static partial Regex Action();

    [GeneratedRegex(@"^\s*image:\s*""?([^\s:""@]+)", RegexOptions.Multiline)]
    private static partial Regex ComposeImage();

    [GeneratedRegex(@"^\s*\w+_IMAGE:\s*""?([^\s:""@]+)", RegexOptions.Multiline)]
    private static partial Regex WorkflowImage();

    [GeneratedRegex(@"\bImage\s*=\s*""([^"":@]+)")]
    private static partial Regex TestImage();
}