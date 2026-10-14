namespace Edvaniq.ArchitectureTests;

// Rule 4 in docs/architecture.md: a .Contracts holds integration events, plain data types that every service can
// reference. It depends on BuildingBlocks.Contracts alone, also through others, and on no framework beyond .NET itself.
internal static class ContractsDependencyRule
{
    private const string BuildingBlocksContracts = "Edvaniq.BuildingBlocks.Contracts";
    private const string DotNet = "Microsoft.NETCore.App";

    public static IEnumerable<string> Violations(IEnumerable<ServiceProject> projects) =>
        projects.Where(project => project.Layer == "Contracts").SelectMany(Check);

    private static IEnumerable<string> Check(ServiceProject project)
    {
        var assets = ProjectAssets.Read(project);
        if (assets is null)
        {
            return [ProjectAssets.NotRestored(project)];
        }

        return assets.Libraries.Where(library => library.Id != BuildingBlocksContracts)
            .Select(library => $"{project.Name} depends on {library.Id}")
            .Concat(assets.Frameworks.Where(framework => framework != DotNet)
                .Select(framework => $"{project.Name} references {framework}"))
            .Distinct();
    }
}