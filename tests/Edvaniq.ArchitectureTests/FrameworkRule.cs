namespace Edvaniq.ArchitectureTests;

// Rule 3 in docs/architecture.md, second half: Domain and Application depend on neither EF Core nor ASP.NET Core, not
// even through another project or package. ProjectAssets lists every dependency of a project, also the inherited ones.
internal static class FrameworkRule
{
    private const string AspNetCore = "Microsoft.AspNetCore.App";

    public static IEnumerable<string> Violations(IEnumerable<ServiceProject> projects) =>
        projects.Where(project => project.Layer is "Domain" or "Application").SelectMany(Check).Distinct();

    private static IEnumerable<string> Check(ServiceProject project)
    {
        var assets = ProjectAssets.Read(project);
        if (assets is null)
        {
            return [ProjectAssets.NotRestored(project)];
        }

        var violations = new List<string>();
        foreach (var library in assets.Libraries)
        {
            if (library.Id.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
                || library.Id.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{project.Name} depends on {library.Id}");
            }

            if (library.Frameworks.Contains(AspNetCore))
            {
                violations.Add($"{project.Name} depends on {library.Id}, which needs {AspNetCore}");
            }
        }

        if (assets.Frameworks.Contains(AspNetCore))
        {
            violations.Add($"{project.Name} references {AspNetCore}");
        }

        return violations;
    }
}