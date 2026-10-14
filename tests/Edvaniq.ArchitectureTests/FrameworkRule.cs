using System.Text.Json;

namespace Edvaniq.ArchitectureTests;

// Rule 3 in docs/architecture.md, second half: Domain and Application depend on neither EF Core nor ASP.NET Core, not
// even through another project or package. The restore writes every dependency of a project, also the inherited ones,
// to obj/project.assets.json: packages and projects under targets, with the frameworks they need, and the project's own
// frameworks under project. The web SDK shows up there as the framework Microsoft.AspNetCore.App.
internal static class FrameworkRule
{
    private const string AspNetCore = "Microsoft.AspNetCore.App";

    public static IEnumerable<string> Violations(IEnumerable<ServiceProject> projects) =>
        projects.Where(project => project.Layer is "Domain" or "Application").SelectMany(Check).Distinct();

    private static IEnumerable<string> Check(ServiceProject project)
    {
        var assets = Path.Combine(Path.GetDirectoryName(project.File)!, "obj", "project.assets.json");
        if (!File.Exists(assets))
        {
            return [$"{project.Name} is not restored, {assets} is missing. Run dotnet restore first."];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var root = document.RootElement;
        var violations = new List<string>();
        foreach (var library in root.GetProperty("targets").EnumerateObject().SelectMany(target => target.Value.EnumerateObject()))
        {
            var id = library.Name.Split('/')[0];
            if (id.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{project.Name} depends on {id}");
            }

            if (library.Value.TryGetProperty("frameworkReferences", out var frameworks)
                && frameworks.EnumerateArray().Any(framework => framework.GetString() == AspNetCore))
            {
                violations.Add($"{project.Name} depends on {id}, which needs {AspNetCore}");
            }
        }

        foreach (var framework in root.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (framework.Value.TryGetProperty("frameworkReferences", out var references) && references.TryGetProperty(AspNetCore, out _))
            {
                violations.Add($"{project.Name} references {AspNetCore}");
            }
        }

        return violations;
    }
}