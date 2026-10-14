using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Edvaniq.ArchitectureTests;

// Rule 5 in docs/architecture.md: a service keeps no copy of data that belongs to another service. Every DbContext in
// a project of a service is built through its design-time factory, as dotnet ef does it, without a database. No entity,
// complex type or property in its model may have a type from another service, not even as a generic argument. Types
// from BuildingBlocks are fine. The projects are loaded from the output of this test project, which references all of
// them.
internal static class ProjectionRule
{
    public static IEnumerable<string> Violations(IEnumerable<ServiceProject> projects) =>
        projects.SelectMany(project => Load(project) is { } assembly
            ? Check(project.Service, assembly.GetTypes())
            : [$"{project.Name} is not referenced by Edvaniq.ArchitectureTests, so its DbContexts stay unchecked"]);

    public static IEnumerable<string> DomainReferences(IEnumerable<ServiceProject> projects) =>
        projects.Where(project => project.Layer == "Domain")
            .SelectMany(project => project.References
                .Where(reference => ServiceProject.Parse(reference)?.Layer == "Contracts")
                .Select(reference => $"{project.Name} references {reference}"));

    public static IEnumerable<string> Check(string service, IEnumerable<Type> types)
    {
        var concrete = types.Where(type => !type.IsAbstract).ToList();
        var factories = concrete
            .Select(type => (Type: type, Interface: type.GetInterfaces().FirstOrDefault(face =>
                face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>))))
            .Where(factory => factory.Interface is not null)
            .ToList();

        var violations = concrete
            .Where(type => typeof(DbContext).IsAssignableFrom(type)
                && factories.All(factory => factory.Interface!.GenericTypeArguments[0] != type))
            .Select(type => $"{type.Name} has no IDesignTimeDbContextFactory, so its model stays unchecked")
            .ToList();
        foreach (var (type, face) in factories)
        {
            var create = face!.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;
            using var context = (DbContext)create.Invoke(Activator.CreateInstance(type), [Array.Empty<string>()])!;
            violations.AddRange(Check(service, context.GetType().Name, context.Model));
        }

        return violations;
    }

    private static IEnumerable<string> Check(string service, string context, IModel model) =>
        model.GetEntityTypes()
            .SelectMany(entity => Types(entity).Prepend((Type: entity.ClrType, Where: entity.DisplayName())))
            .SelectMany(found => Parts(found.Type).Select(type => (Type: type, found.Where)))
            .Select(found => (found.Type, found.Where, Owner: ServiceProject.Parse(found.Type.Assembly.GetName().Name!)?.Service))
            .Where(found => found.Owner is not null && found.Owner != service)
            .Select(found => $"{context} maps {found.Type.FullName} from {found.Owner} at {found.Where}")
            .Distinct()
            .Order(StringComparer.Ordinal);

    // The properties of an entity or complex type and its complex properties, down to the last level.
    private static IEnumerable<(Type Type, string Where)> Types(ITypeBase type) =>
        type.GetProperties().Select(property => (property.ClrType, $"{type.DisplayName()}.{property.Name}"))
            .Concat(type.GetComplexProperties().SelectMany(complex =>
                Types(complex.ComplexType).Prepend((Type: complex.ClrType, Where: $"{type.DisplayName()}.{complex.Name}"))));

    // A type with its generic arguments and element types, so List<T>, T[] and T? all show T.
    private static IEnumerable<Type> Parts(Type type) =>
        type.HasElementType ? Parts(type.GetElementType()!) : type.GenericTypeArguments.SelectMany(Parts).Prepend(type);

    private static Assembly? Load(ServiceProject project)
    {
        try
        {
            return Assembly.Load(new AssemblyName(project.Name));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}