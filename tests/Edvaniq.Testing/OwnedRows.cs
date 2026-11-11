using System.Reflection;

using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Testing;

// How many rows of a user every table with an owner holds, read from the model, so a new table counts without a change
// here. Only reads, past the owner filter, whoever the test's current user is.
public static class OwnedRows
{
    private static readonly MethodInfo CountOf = typeof(OwnedRows).GetMethod(nameof(Count), BindingFlags.NonPublic | BindingFlags.Static)!;

    // By the name of the entity, ordered.
    public static SortedDictionary<string, int> Of(DbContext db, string owner) =>
        new(db.Model.GetEntityTypes()
            .Where(entity => !entity.IsOwned() && typeof(IOwnedByUser).IsAssignableFrom(entity.ClrType))
            .ToDictionary(entity => entity.ClrType.Name, entity => (int)CountOf.MakeGenericMethod(entity.ClrType).Invoke(null, [db, owner])!),
            StringComparer.Ordinal);

    // The tables without a row of the user. A test that shows all of them go needs a row in each, so it asserts this is
    // empty before it deletes: a new table without a seed fails it.
    public static IReadOnlyList<string> Unseeded(IReadOnlyDictionary<string, int> rows) =>
        [.. rows.Where(table => table.Value == 0).Select(table => table.Key)];

    private static int Count<T>(DbContext db, string owner) where T : class, IOwnedByUser =>
        db.Set<T>().IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).AsNoTracking().Count(row => row.OwnerId == owner);
}