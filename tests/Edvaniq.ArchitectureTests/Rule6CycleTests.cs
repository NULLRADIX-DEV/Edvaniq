namespace Edvaniq.ArchitectureTests;

// Rule 6 in docs/architecture.md: integration events form no cycle, neither in the project references between the
// services nor in the table "Wer auf wessen Integration Events hört".
public class Rule6CycleTests
{
    [Fact]
    public void ServiceReferences_FormNoCycle() =>
        Rules.AssertNone(CycleRule.Violations(ServiceProject.ReadAll(Repository.Root)), "Rule 6");

    [Fact]
    public void EventTable_HasNoCycle()
    {
        var edges = CycleRule.Table(File.ReadAllText(Path.Combine(Repository.Root, "docs", "architecture.md")));

        Rules.AssertNone(CycleRule.Cycles(edges), "Rule 6");
        Assert.Contains(("LearningEngine", "Planning"), edges);
    }

    [Fact]
    public void TwoServicesOnEachOthersContracts_AreReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Application", "Edvaniq.Services.LearningEngine.Contracts")
            .Project("LearningEngine", "Edvaniq.Services.LearningEngine.Application", "Edvaniq.Services.Planning.Contracts");

        var violations = CycleRule.Violations(services.Read());

        Assert.Equal(["LearningEngine -> Planning -> LearningEngine"], violations);
    }

    [Fact]
    public void CycleOverThreeServices_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Application", "Edvaniq.Services.Content.Contracts")
            .Project("Content", "Edvaniq.Services.Content.Application", "Edvaniq.Services.Knowledge.Contracts")
            .Project("Knowledge", "Edvaniq.Services.Knowledge.Application", "Edvaniq.Services.Planning.Contracts");

        var violations = CycleRule.Violations(services.Read());

        Assert.Equal(["Content -> Knowledge -> Planning -> Content"], violations);
    }

    [Fact]
    public void ReferencesWithoutCycle_AreNotReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Application",
                "Edvaniq.Services.Content.Contracts", "Edvaniq.Services.Knowledge.Contracts", "Edvaniq.Services.Planning.Contracts")
            .Project("Knowledge", "Edvaniq.Services.Knowledge.Application", "Edvaniq.Services.Content.Contracts");

        Assert.Empty(CycleRule.Violations(services.Read()));
    }

    [Fact]
    public void TableWithACycle_IsReported()
    {
        var edges = CycleRule.Table("""
            | Consumer | konsumiert von |
            |---|---|
            | Planning | Identity, LearningEngine |
            | LearningEngine | Identity, Planning |

            | Andere Tabelle | danach |
            """);

        Assert.Equal(
            [("Planning", "Identity"), ("Planning", "LearningEngine"), ("LearningEngine", "Identity"), ("LearningEngine", "Planning")],
            edges);
        Assert.Equal(["LearningEngine -> Planning -> LearningEngine"], CycleRule.Cycles(edges));
    }
}