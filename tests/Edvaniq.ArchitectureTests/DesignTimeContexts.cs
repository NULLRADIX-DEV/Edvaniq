using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Edvaniq.ArchitectureTests;

// The DbContexts among some types, each built through its design-time factory as dotnet ef does it, without a
// database. A DbContext without a factory comes back as a name only, its model stays unchecked.
internal static class DesignTimeContexts
{
    public static (List<string> WithoutFactory, List<Func<DbContext>> Create) Find(IEnumerable<Type> types)
    {
        var concrete = types.Where(type => !type.IsAbstract).ToList();
        var factories = concrete
            .Select(type => (Type: type, Interface: type.GetInterfaces().FirstOrDefault(face =>
                face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>))))
            .Where(factory => factory.Interface is not null)
            .ToList();

        var withoutFactory = concrete
            .Where(type => typeof(DbContext).IsAssignableFrom(type)
                && factories.All(factory => factory.Interface!.GenericTypeArguments[0] != type))
            .Select(type => type.Name)
            .ToList();
        var create = factories
            .Select(factory => (Func<DbContext>)(() =>
            {
                var method = factory.Interface!.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;
                return (DbContext)method.Invoke(Activator.CreateInstance(factory.Type), [Array.Empty<string>()])!;
            }))
            .ToList();
        return (withoutFactory, create);
    }
}