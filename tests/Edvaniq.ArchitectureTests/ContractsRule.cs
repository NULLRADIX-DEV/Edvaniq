namespace Edvaniq.ArchitectureTests;

// Rule 2 in docs/architecture.md: from another service, a project of a service references only its .Contracts.
internal static class ContractsRule
{
    public static IEnumerable<string> Violations(IEnumerable<ServiceProject> projects) =>
        from project in projects
        from reference in project.References
        let target = ServiceProject.Parse(reference)
        where target is { } other && other.Service != project.Service && other.Layer != "Contracts"
        select $"{project.Name} references {reference}";
}