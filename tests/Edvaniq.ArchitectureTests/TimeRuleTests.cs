using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Edvaniq.ArchitectureTests;

// The time rule in docs/architecture.md ("Weitere Regeln"): points in time in UTC, the study day only from
// Edvaniq.SharedKernel, which depends on nothing.
public class TimeRuleTests
{
    [Fact]
    public void DateTimeColumns_HoldUtc() =>
        Rules.AssertNone(TimeRule.ColumnViolations(ServiceProject.ReadAll(Repository.Root)), "The time rule");

    [Fact]
    public void Models_MatchTheirLastMigration() =>
        Rules.AssertNone(TimeRule.PendingModelChanges(ServiceProject.ReadAll(Repository.Root)), "The time rule");

    [Fact]
    public void Code_ReadsNeitherTheMachineClockNorATimeZone() =>
        Rules.AssertNone(TimeRule.SourceViolations(Repository.Root), "The time rule");

    [Fact]
    public void SharedKernel_DependsOnNothing_AndOnlyDomainsUseIt() =>
        Rules.AssertNone(SharedKernelRule.Violations(Repository.Root), "The time rule");

    [Fact]
    public void ColumnsOfAContext_AreReported()
    {
        var violations = TimeRule.CheckColumns([typeof(PlainDbContext), typeof(PlainDbContextFactory)]);

        Assert.Equal(
            [
                "PlainDbContext Entry.At has no UtcDateTimeConverter, so it may not hold UTC",
                "PlainDbContext Entry.Day is a TimeOnly, a point in time is a DateTime in UTC",
                "PlainDbContext Entry.Offset is a DateTimeOffset, a point in time is a DateTime in UTC",
                "PlainDbContext Entry.Until has no UtcDateTimeConverter, so it may not hold UTC",
            ],
            violations);
    }

    [Fact]
    public void ServiceDbContext_GivesEveryDateTimeTheConverter()
    {
        using var context = new TimedDbContextFactory().CreateDbContext([]);
        var entry = context.Model.FindEntityType(typeof(Entry))!;

        Assert.IsType<UtcDateTimeConverter>(entry.FindProperty(nameof(Entry.At))!.GetValueConverter());
        Assert.IsType<UtcDateTimeConverter>(entry.FindProperty(nameof(Entry.Until))!.GetValueConverter());
        Assert.Equal(["TimedDbContext Entry.Day is a TimeOnly, a point in time is a DateTime in UTC",
            "TimedDbContext Entry.Offset is a DateTimeOffset, a point in time is a DateTime in UTC"],
            TimeRule.CheckColumns([typeof(TimedDbContext), typeof(TimedDbContextFactory)]));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Converter_StoresUtcAndUnspecifiedUnchanged(DateTimeKind kind)
    {
        var value = new DateTime(2027, 3, 28, 1, 30, 0, kind);

        var stored = (DateTime)new UtcDateTimeConverter().ConvertToProvider(value)!;

        Assert.Equal(value.Ticks, stored.Ticks);
    }

    [Fact]
    public void Converter_RefusesLocalTime() =>
        Assert.Throws<ArgumentException>(() =>
            new UtcDateTimeConverter().ConvertToProvider(new DateTime(2027, 3, 28, 1, 30, 0, DateTimeKind.Local)));

    [Fact]
    public void Converter_ReadsBackAsUtc()
    {
        var read = (DateTime)new UtcDateTimeConverter().ConvertFromProvider(new DateTime(2027, 3, 28, 1, 30, 0))!;

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(new DateTime(2027, 3, 28, 1, 30, 0, DateTimeKind.Utc), read);
    }

    [Fact]
    public void ClockAndZoneInCode_AreReported()
    {
        var violations = TimeRule.CheckSource("Sample.cs",
        [
            "var a = DateTime.Now;",
            "var b = DateTimeOffset.UtcNow;",
            "var c = DateTime.Today;",
            "var d = TimeZoneInfo.FindSystemTimeZoneById(\"Europe/Berlin\");",
            "var e = time.ToLocalTime();",
            "// DateTime.Now in a comment is fine",
            "var f = timeProvider.GetUtcNow();",
        ]);

        Assert.Equal(
            [
                "Sample.cs:1 uses DateTime.Now",
                "Sample.cs:2 uses DateTimeOffset.UtcNow",
                "Sample.cs:3 uses DateTime.Today",
                "Sample.cs:4 uses TimeZoneInfo",
                "Sample.cs:4 uses Europe/Berlin",
                "Sample.cs:5 uses .ToLocalTime(",
            ],
            violations);
    }

    [Fact]
    public void SharedKernelWithReferencesOrUsers_IsReported()
    {
        var root = Directory.CreateTempSubdirectory("edvaniq-architecture-").FullName;
        try
        {
            Write(root, "src/SharedKernel/Edvaniq.SharedKernel/Edvaniq.SharedKernel.csproj",
                """<PackageReference Include="Some.Package" />""");
            Write(root, "src/Services/Planning/Edvaniq.Services.Planning.Domain/Edvaniq.Services.Planning.Domain.csproj",
                """<ProjectReference Include="..\..\..\SharedKernel\Edvaniq.SharedKernel\Edvaniq.SharedKernel.csproj" />""");
            Write(root, "src/Aspire/Edvaniq.ServiceDefaults/Edvaniq.ServiceDefaults.csproj",
                """<ProjectReference Include="..\..\SharedKernel\Edvaniq.SharedKernel\Edvaniq.SharedKernel.csproj" />""");
            Write(root, "src/BuildingBlocks/Edvaniq.BuildingBlocks.Domain/Edvaniq.BuildingBlocks.Domain.csproj",
                """<ProjectReference Include="..\..\SharedKernel\Edvaniq.SharedKernel\Edvaniq.SharedKernel.csproj" />""");

            Assert.Equal(
                [
                    "Edvaniq.BuildingBlocks.Domain references Edvaniq.SharedKernel, only a Domain and the ServiceDefaults may",
                    "Edvaniq.SharedKernel has the PackageReference Some.Package",
                ],
                SharedKernelRule.Violations(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string path, string item)
    {
        var file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                {item}
              </ItemGroup>
            </Project>
            """);
    }

    private sealed class Entry
    {
        public int Id { get; set; }

        public DateTime At { get; set; }

        public DateTime? Until { get; set; }

        public DateTimeOffset Offset { get; set; }

        public TimeOnly Day { get; set; }

        public DateOnly Date { get; set; }
    }

    private sealed class PlainDbContext(DbContextOptions<PlainDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Entry>();
    }

    private sealed class PlainDbContextFactory : IDesignTimeDbContextFactory<PlainDbContext>
    {
        public PlainDbContext CreateDbContext(string[] args) =>
            new(new DbContextOptionsBuilder<PlainDbContext>().UseMySQL("Server=localhost;Database=sampledb").Options);
    }

    private sealed class TimedDbContext(DbContextOptions<TimedDbContext> options)
        : ServiceDbContext(options, new NoUser())
    {
        protected override void ConfigureModel(ModelBuilder modelBuilder) => modelBuilder.Entity<Entry>();
    }

    private sealed class TimedDbContextFactory : IDesignTimeDbContextFactory<TimedDbContext>
    {
        public TimedDbContext CreateDbContext(string[] args) =>
            new(new DbContextOptionsBuilder<TimedDbContext>().UseMySQL("Server=localhost;Database=sampledb").Options);
    }

    private sealed class NoUser : Edvaniq.BuildingBlocks.Application.ICurrentUser
    {
        public string Id => throw new InvalidOperationException("No user at design time.");
    }
}