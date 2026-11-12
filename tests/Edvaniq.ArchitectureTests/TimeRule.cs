using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Edvaniq.ArchitectureTests;

// The time rule in docs/architecture.md ("Weitere Regeln"). Every DateTime column holds UTC through the convention of
// ServiceDbContext, every DateOnly reads back through its DateOnlyConverter, and no model keeps a DateTimeOffset or
// TimeOnly. The code of the services, the BuildingBlocks and
// the service template never reads the clock or the zone of the machine and never works with a time zone itself:
// the clock is TimeProvider, the study day comes only from Edvaniq.SharedKernel.StudyDay.
internal static partial class TimeRule
{
    private static readonly string[] SourceFolders = ["src/Services", "src/BuildingBlocks", "templates/service/src"];

    public static IEnumerable<string> ColumnViolations(IEnumerable<ServiceProject> projects) =>
        projects.Where(project => project.Layer == "Infrastructure")
            .SelectMany(project => project.Load() is { } assembly
                ? CheckColumns(assembly.GetTypes())
                : [$"{project.Name} is not referenced by Edvaniq.ArchitectureTests, so its DbContexts stay unchecked"]);

    public static IEnumerable<string> CheckColumns(IEnumerable<Type> types)
    {
        foreach (var create in DesignTimeContexts.Find(types).Create)
        {
            using var context = create();
            foreach (var violation in CheckColumns(context.GetType().Name, context.Model))
            {
                yield return violation;
            }
        }
    }

    // Compares the model with the snapshot of the last migration, like dotnet ef migrations has-pending-model-changes.
    public static IEnumerable<string> PendingModelChanges(IEnumerable<ServiceProject> projects) =>
        projects.Where(project => project.Layer == "Infrastructure")
            .Select(project => project.Load())
            .OfType<System.Reflection.Assembly>()
            .SelectMany(assembly => DesignTimeContexts.Find(assembly.GetTypes()).Create)
            .Select(create =>
            {
                using var context = create();
                return context.Database.HasPendingModelChanges() ? $"{context.GetType().Name} differs from its last migration" : null;
            })
            .OfType<string>();

    public static IEnumerable<string> SourceViolations(string root) =>
        SourceFolders.Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(path => CheckSource(path, File.ReadAllLines(Path.Combine(root, path))));

    public static IEnumerable<string> CheckSource(string path, string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var start = lines[index].TrimStart();
            if (start.StartsWith("//", StringComparison.Ordinal) || start.StartsWith('*'))
            {
                continue;
            }

            foreach (var found in Forbidden().Matches(lines[index]).Select(match => match.Value))
            {
                yield return $"{path}:{index + 1} uses {found}";
            }
        }
    }

    private static IEnumerable<string> CheckColumns(string context, IModel model) =>
        model.GetEntityTypes()
            .SelectMany(entity => Properties(entity))
            .SelectMany(property => CheckColumn(context, property))
            .Distinct()
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> CheckColumn(string context, IProperty property)
    {
        var where = $"{context} {property.DeclaringType.DisplayName()}.{property.Name}";
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (type == typeof(DateTimeOffset) || type == typeof(TimeOnly))
        {
            yield return $"{where} is a {type.Name}, a point in time is a DateTime in UTC";
        }
        else if (type == typeof(DateTime) && property.GetValueConverter() is not UtcDateTimeConverter)
        {
            yield return $"{where} has no UtcDateTimeConverter, so it may not hold UTC";
        }
        else if (type == typeof(DateOnly) && property.GetValueConverter() is not DateOnlyConverter)
        {
            yield return $"{where} has no DateOnlyConverter, so MySQL cannot read it back";
        }
    }

    private static IEnumerable<IProperty> Properties(ITypeBase type) =>
        type.GetProperties().Concat(type.GetComplexProperties().SelectMany(complex => Properties(complex.ComplexType)));

    [GeneratedRegex(@"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b|\bTimeZoneInfo\b|\.ToLocalTime\s*\(|Europe/Berlin")]
    private static partial Regex Forbidden();
}