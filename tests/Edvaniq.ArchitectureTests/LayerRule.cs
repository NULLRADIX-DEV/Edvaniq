namespace Edvaniq.ArchitectureTests;

// Rule 3 in docs/architecture.md, first half: a project of a service references directly only what the table
// "Schichten eines Service" allows for its layer. A project whose layer is not in the table is a violation too, so a new
// kind of project is added to the table first.
internal static class LayerRule
{
    // The column "darf referenzieren" of the table. {own} stands for Edvaniq.Services.<Service> of the same service.
    private static readonly Dictionary<string, Layer> Layers = new()
    {
        ["Domain"] = new(["Edvaniq.BuildingBlocks.Domain"]),
        ["Application"] = new(
            ["{own}.Domain", "{own}.Contracts", "Edvaniq.BuildingBlocks.Application"], ForeignContracts: true),
        ["Infrastructure"] = new(
            ["{own}.Application", "Edvaniq.BuildingBlocks.Infrastructure", "Edvaniq.BuildingBlocks.Messaging", "Edvaniq.BuildingBlocks.AI"]),
        ["Api"] = new(["{own}.Application", "{own}.Infrastructure", "Edvaniq.BuildingBlocks.Web", "Edvaniq.ServiceDefaults"]),
        ["Contracts"] = new(["Edvaniq.BuildingBlocks.Contracts"]),
        ["Worker"] = new(["{own}.Application", "{own}.Infrastructure", "Edvaniq.ServiceDefaults"]),
    };

    public static IEnumerable<string> Violations(IEnumerable<ServiceProject> projects)
    {
        foreach (var project in projects)
        {
            if (project.Layer is null || !Layers.TryGetValue(project.Layer, out var layer))
            {
                yield return $"{project.Name} has no layer from the table, expected {ServiceProject.Prefix}{project.Service}.<Layer>";
                continue;
            }

            var own = ServiceProject.Prefix + project.Service;
            foreach (var reference in project.References.Where(reference => !layer.Allows(own, reference)))
            {
                yield return $"{project.Name} ({project.Layer}) references {reference}";
            }
        }
    }

    private sealed record Layer(string[] References, bool ForeignContracts = false)
    {
        public bool Allows(string own, string reference) =>
            References.Any(allowed => allowed.Replace("{own}", own, StringComparison.Ordinal) == reference)
            || (ForeignContracts && !reference.StartsWith(own + ".", StringComparison.Ordinal)
                && ServiceProject.Parse(reference) is { Layer: "Contracts" });
    }
}