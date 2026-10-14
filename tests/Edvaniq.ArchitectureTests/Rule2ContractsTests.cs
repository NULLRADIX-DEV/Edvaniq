namespace Edvaniq.ArchitectureTests;

// Rule 2 in docs/architecture.md: from another service, a project of a service references only its .Contracts.
public class Rule2ContractsTests
{
    [Fact]
    public void ServiceProjects_ReferenceOnlyTheContractsOfOtherServices() =>
        Rules.AssertNone(ContractsRule.Violations(ServiceProject.ReadAll(Repository.Root)), "Rule 2");

    [Fact]
    public void ReadAll_FindsTheProjectsOfAServiceWithTheirReferences()
    {
        var projects = ServiceProject.ReadAll(Repository.Root);

        var planning = projects.Where(project => project.Service == "Planning").Select(project => project.Layer).Order();
        Assert.Equal(["Api", "Application", "Contracts", "Domain", "Infrastructure"], planning);
        Assert.Contains(
            "Edvaniq.Services.Planning.Application",
            projects.Single(project => project.Name == "Edvaniq.Services.Planning.Api").References);
    }

    [Fact]
    public void ReferenceToTheDomainOfAnotherService_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Application", "Edvaniq.Services.Planning.Domain", "Edvaniq.Services.Content.Domain");

        var violations = ContractsRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Application references Edvaniq.Services.Content.Domain"], violations);
    }

    [Fact]
    public void ReferenceToTheApiOfAnotherService_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Api", "Edvaniq.Services.Identity.Api");

        var violations = ContractsRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Api references Edvaniq.Services.Identity.Api"], violations);
    }

    [Fact]
    public void ReferencesToOwnProjectsAndForeignContracts_AreNotReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Application",
                "Edvaniq.Services.Planning.Domain", "Edvaniq.Services.Content.Contracts", "Edvaniq.BuildingBlocks.Application");

        Assert.Empty(ContractsRule.Violations(services.Read()));
    }
}