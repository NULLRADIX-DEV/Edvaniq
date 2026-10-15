using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Edvaniq.ArchitectureTests;

// Rule 5 in docs/architecture.md: no projections of foreign data. No DbContext of a service maps a type from another
// service, and no Domain references a .Contracts.
public class Rule5ProjectionTests
{
    [Fact]
    public void DbContexts_MapNoTypeOfAnotherService() =>
        Rules.AssertNone(ProjectionRule.Violations(ServiceProject.ReadAll(Repository.Root)), "Rule 5");

    [Fact]
    public void Domains_ReferenceNoContracts() =>
        Rules.AssertNone(ProjectionRule.DomainReferences(ServiceProject.ReadAll(Repository.Root)), "Rule 5");

    [Fact]
    public void PlanningDbContext_InAnotherService_IsReported()
    {
        var violations = ProjectionRule.Check("Content", typeof(PlanningDbContext).Assembly.GetTypes());

        Assert.Equal(
            [
                "PlanningDbContext maps Edvaniq.Services.Planning.Domain.ExampleItem from Planning at ExampleItem",
                "PlanningDbContext maps Edvaniq.Services.Planning.Domain.PingRecord from Planning at PingRecord",
            ],
            violations);
    }

    [Fact]
    public void PlanningDbContext_InPlanning_IsNotReported() =>
        Assert.Empty(ProjectionRule.Check("Planning", typeof(PlanningDbContext).Assembly.GetTypes()));

    [Fact]
    public void ForeignTypesInsideOwnEntities_AreReported()
    {
        var violations = ProjectionRule.Check("Content", [typeof(WrappingDbContext), typeof(WrappingDbContextFactory)]);

        Assert.Equal(
            [
                "WrappingDbContext maps Edvaniq.Services.Planning.Domain.ExampleItem from Planning at Note.Copies",
                "WrappingDbContext maps Edvaniq.Services.Planning.Domain.ExampleItem from Planning at Note.Copy",
            ],
            violations);
    }

    [Fact]
    public void DbContextWithoutDesignTimeFactory_IsReported()
    {
        var violations = ProjectionRule.Check("Content", [typeof(WrappingDbContext)]);

        Assert.Equal(["WrappingDbContext has no IDesignTimeDbContextFactory, so its model stays unchecked"], violations);
    }

    [Fact]
    public void ProjectNotReferencedByTheTests_IsReported()
    {
        using var services = new FakeServices().Project("Sample", "Edvaniq.Services.Sample.Infrastructure");

        var violations = ProjectionRule.Violations(services.Read());

        Assert.Equal(["Edvaniq.Services.Sample.Infrastructure is not referenced by Edvaniq.ArchitectureTests, so its DbContexts stay unchecked"], violations);
    }

    [Fact]
    public void DomainReferencingContracts_IsReported()
    {
        using var services = new FakeServices()
            .Project("Planning", "Edvaniq.Services.Planning.Domain", "Edvaniq.BuildingBlocks.Domain", "Edvaniq.Services.Content.Contracts")
            .Project("Planning", "Edvaniq.Services.Planning.Application", "Edvaniq.Services.Content.Contracts");

        var violations = ProjectionRule.DomainReferences(services.Read());

        Assert.Equal(["Edvaniq.Services.Planning.Domain references Edvaniq.Services.Content.Contracts"], violations);
    }

    // An entity of its own that keeps a copy of Planning's ExampleItem as a complex type and a list of them in a column.
    private sealed class Note
    {
        public int Id { get; set; }

        public ExampleItem Copy { get; set; } = new("owner", "text");

        public List<ExampleItem> Copies { get; set; } = [];
    }

    private sealed class WrappingDbContext(DbContextOptions<WrappingDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var note = modelBuilder.Entity<Note>();
            note.ComplexProperty(item => item.Copy);
            note.Property(item => item.Copies).HasConversion(copies => copies.Count, count => new List<ExampleItem>());
        }
    }

    private sealed class WrappingDbContextFactory : IDesignTimeDbContextFactory<WrappingDbContext>
    {
        public WrappingDbContext CreateDbContext(string[] args) =>
            new(new DbContextOptionsBuilder<WrappingDbContext>().UseMySQL("Server=localhost;Database=contentdb").Options);
    }
}