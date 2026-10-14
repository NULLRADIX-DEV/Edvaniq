namespace Edvaniq.ArchitectureTests;

// Rule 4 in docs/architecture.md: services exchange data only through integration events or HTTP queries, and the
// address of another service comes from outside. A .Contracts depends on BuildingBlocks.Contracts alone, and no code or
// settings under src/Services hold an address.
public class Rule4IntegrationTests
{
    [Fact]
    public void Contracts_DependOnlyOnBuildingBlocksContracts() =>
        Rules.AssertNone(ContractsDependencyRule.Violations(ServiceProject.ReadAll(Repository.Root)), "Rule 4");

    [Fact]
    public void ServiceCodeAndSettings_HoldNoAddress() =>
        Rules.AssertNone(AddressRule.Violations(Repository.Root), "Rule 4");

    [Fact]
    public void ContractsOnBuildingBlocksContracts_AreNotReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Contracts", "Edvaniq.BuildingBlocks.Contracts")
            .Assets("Planning", "Edvaniq.Services.Planning.Contracts", """
                "Edvaniq.BuildingBlocks.Contracts/1.0.0": { "type": "project" }
                """);

        Assert.Empty(ContractsDependencyRule.Violations(services.Read()));
    }

    [Fact]
    public void ContractsWithAPackage_AreReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Contracts")
            .Assets("Planning", "Edvaniq.Services.Planning.Contracts", """
                "Edvaniq.BuildingBlocks.Contracts/1.0.0": { "type": "project" },
                "Microsoft.EntityFrameworkCore/10.0.12": { "type": "package" }
                """);

        var violations = ContractsDependencyRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Contracts depends on Microsoft.EntityFrameworkCore"], violations);
    }

    [Fact]
    public void ContractsOnAnotherProject_AreReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Contracts")
            .Assets("Planning", "Edvaniq.Services.Planning.Contracts", """
                "Edvaniq.BuildingBlocks.Contracts/1.0.0": { "type": "project" },
                "Edvaniq.Services.Planning.Domain/1.0.0": { "type": "project" }
                """);

        var violations = ContractsDependencyRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Contracts depends on Edvaniq.Services.Planning.Domain"], violations);
    }

    [Fact]
    public void ContractsOnTheWebSdk_AreReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Contracts")
            .Assets("Planning", "Edvaniq.Services.Planning.Contracts",
                frameworks: "\"Microsoft.AspNetCore.App\": {}, \"Microsoft.NETCore.App\": {}");

        var violations = ContractsDependencyRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Contracts references Microsoft.AspNetCore.App"], violations);
    }

    [Fact]
    public void ContractsWithoutRestore_AreReported()
    {
        using var services = new FakeServices().Project("Planning", "Edvaniq.Services.Planning.Contracts");

        var violation = Assert.Single(ContractsDependencyRule.Violations(services.Read()));

        Assert.StartsWith("Edvaniq.Services.Planning.Contracts is not restored", violation);
    }

    [Fact]
    public void AddressesInCodeAndSettings_AreReported()
    {
        using var services = new FakeServices()
            .Source("Planning", "Edvaniq.Services.Planning.Infrastructure", "Mastery.cs", """
                class Mastery
                {
                    Uri Address = new("http://learningengine-api:8080");
                    string Host = "10.0.0.7";
                    string Local = "Server=localhost";
                }
                """)
            .Source("Planning", "Edvaniq.Services.Planning.Api", "appsettings.Production.json", """
                { "Broker": "amqp://broker.example.org" }
                """);

        var violations = AddressRule.Violations(services.Root);

        Assert.Equal(
            [
                "src/Services/Planning/Edvaniq.Services.Planning.Api/appsettings.Production.json:1 holds the address amqp://broker.example.org",
                "src/Services/Planning/Edvaniq.Services.Planning.Infrastructure/Mastery.cs:3 holds the address http://learningengine-api:8080",
                "src/Services/Planning/Edvaniq.Services.Planning.Infrastructure/Mastery.cs:4 holds the address 10.0.0.7",
                "src/Services/Planning/Edvaniq.Services.Planning.Infrastructure/Mastery.cs:5 holds the address localhost",
            ],
            violations);
    }

    [Fact]
    public void DiscoveryNamesCommentsDesignTimeFactoriesAndBuildOutput_AreNotReported()
    {
        using var services = new FakeServices()
            .Source("Planning", "Edvaniq.Services.Planning.Infrastructure", "Mastery.cs", """
                // Like https://learn.microsoft.com/dotnet/aspire/service-discovery/overview
                class Mastery
                {
                    Uri Address = new("https+http://learningengine-api");
                }
                """)
            .Source("Planning", "Edvaniq.Services.Planning.Infrastructure", "PlanningDbContextFactory.cs", """
                class PlanningDbContextFactory : IDesignTimeDbContextFactory<PlanningDbContext>
                {
                    string Placeholder = "Server=localhost;Database=planningdb";
                }
                """)
            .Source("Planning", "Edvaniq.Services.Planning.Api", "obj/Debug/Generated.cs", "string Url = \"http://localhost:5000\";")
            .Source("Planning", "Edvaniq.Services.Planning.Api", "bin/Debug/appsettings.json", "{ \"Url\": \"http://localhost:5000\" }")
            .Source("Planning", "Edvaniq.Services.Planning.Api", "Properties/launchSettings.json", "{ \"applicationUrl\": \"http://localhost:5000\" }");

        Assert.Empty(AddressRule.Violations(services.Root));
    }

    [Fact]
    public void DiscoveryNamesWithAFixedHost_AreReported()
    {
        using var services = new FakeServices()
            .Source("Planning", "Edvaniq.Services.Planning.Infrastructure", "Mastery.cs", """
                Uri[] Addresses = [new("https+http://localhost"), new("https+http://10.0.0.7"), new("https+http://learningengine.example.org")];
                """);

        var violations = AddressRule.Violations(services.Root);

        Assert.Equal(
            [
                "src/Services/Planning/Edvaniq.Services.Planning.Infrastructure/Mastery.cs:1 holds the address https+http://localhost",
                "src/Services/Planning/Edvaniq.Services.Planning.Infrastructure/Mastery.cs:1 holds the address https+http://10.0.0.7",
                "src/Services/Planning/Edvaniq.Services.Planning.Infrastructure/Mastery.cs:1 holds the address https+http://learningengine.example.org",
            ],
            violations);
    }
}