namespace Edvaniq.ArchitectureTests;

// Rule 3 in docs/architecture.md: layers point inward. A project references directly only what the table "Schichten eines
// Service" allows, and Domain and Application depend on neither EF Core nor ASP.NET Core, not even through another one.
public class Rule3LayerTests
{
    [Fact]
    public void ServiceProjects_ReferenceOnlyWhatTheirLayerAllows() =>
        Rules.AssertNone(LayerRule.Violations(ServiceProject.ReadAll(Repository.Root)), "Rule 3");

    [Fact]
    public void DomainAndApplication_DependOnNeitherEfCoreNorAspNetCore() =>
        Rules.AssertNone(FrameworkRule.Violations(ServiceProject.ReadAll(Repository.Root)), "Rule 3");

    [Fact]
    public void ProjectsAsTheTableAllows_AreNotReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Domain", "Edvaniq.BuildingBlocks.Domain")
            .Project("Planning", "Edvaniq.Services.Planning.Application",
                "Edvaniq.Services.Planning.Domain", "Edvaniq.Services.Planning.Contracts", "Edvaniq.BuildingBlocks.Application",
                "Edvaniq.Services.Content.Contracts")
            .Project("Planning", "Edvaniq.Services.Planning.Infrastructure",
                "Edvaniq.Services.Planning.Application", "Edvaniq.BuildingBlocks.Infrastructure", "Edvaniq.BuildingBlocks.Messaging",
                "Edvaniq.BuildingBlocks.AI")
            .Project("Planning", "Edvaniq.Services.Planning.Api",
                "Edvaniq.Services.Planning.Application", "Edvaniq.Services.Planning.Infrastructure", "Edvaniq.BuildingBlocks.Web",
                "Edvaniq.ServiceDefaults")
            .Project("Planning", "Edvaniq.Services.Planning.Contracts", "Edvaniq.BuildingBlocks.Contracts")
            .Project("Planning", "Edvaniq.Services.Planning.Worker",
                "Edvaniq.Services.Planning.Application", "Edvaniq.Services.Planning.Infrastructure", "Edvaniq.ServiceDefaults");

        Assert.Empty(LayerRule.Violations(services.Read()));
    }

    [Fact]
    public void DomainReferencingItsInfrastructure_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Domain", "Edvaniq.BuildingBlocks.Domain", "Edvaniq.Services.Planning.Infrastructure");

        var violations = LayerRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Domain (Domain) references Edvaniq.Services.Planning.Infrastructure"], violations);
    }

    [Fact]
    public void ForeignContractsOutsideApplication_AreReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Infrastructure",
                "Edvaniq.Services.Planning.Application", "Edvaniq.Services.Content.Contracts");

        var violations = LayerRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Infrastructure (Infrastructure) references Edvaniq.Services.Content.Contracts"], violations);
    }

    [Fact]
    public void ProjectWithoutALayerFromTheTable_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Tools")
            .Project("Planning", "Edvaniq.Services.Content.Domain");

        var violations = LayerRule.Violations(services.Read());

        Assert.Equal(
            [
                "Edvaniq.Services.Content.Domain has no layer from the table, expected Edvaniq.Services.Planning.<Layer>",
                "Edvaniq.Services.Planning.Tools has no layer from the table, expected Edvaniq.Services.Planning.<Layer>",
            ],
            violations);
    }

    [Fact]
    public void ApplicationWithEfCoreThroughAnotherPackage_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Application")
            .Assets("Planning", "Edvaniq.Services.Planning.Application", """
                "Some.Package/1.0.0": { "type": "package" },
                "Microsoft.EntityFrameworkCore/10.0.12": { "type": "package" }
                """);

        var violations = FrameworkRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Application depends on Microsoft.EntityFrameworkCore"], violations);
    }

    [Fact]
    public void DomainWithAspNetCoreThroughAProject_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Domain")
            .Assets("Planning", "Edvaniq.Services.Planning.Domain", """
                "Edvaniq.BuildingBlocks.Web/1.0.0": { "type": "project", "frameworkReferences": [ "Microsoft.AspNetCore.App" ] }
                """);

        var violations = FrameworkRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Domain depends on Edvaniq.BuildingBlocks.Web, which needs Microsoft.AspNetCore.App"], violations);
    }

    [Fact]
    public void ApplicationOnTheWebSdk_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Application")
            .Assets("Planning", "Edvaniq.Services.Planning.Application",
                frameworks: "\"Microsoft.AspNetCore.App\": {}, \"Microsoft.NETCore.App\": {}");

        var violations = FrameworkRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Application references Microsoft.AspNetCore.App"], violations);
    }

    [Fact]
    public void ApplicationWithoutRestore_IsReported()
    {
        using var services = new FakeServices().Project("Planning", "Edvaniq.Services.Planning.Application");

        var violation = Assert.Single(FrameworkRule.Violations(services.Read()));

        Assert.StartsWith("Edvaniq.Services.Planning.Application is not restored", violation);
    }

    [Fact]
    public void InfrastructureWithEfCore_IsNotReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Infrastructure")
            .Assets("Planning", "Edvaniq.Services.Planning.Infrastructure", """
                "Microsoft.EntityFrameworkCore/10.0.12": { "type": "package" }
                """);

        Assert.Empty(FrameworkRule.Violations(services.Read()));
    }
}