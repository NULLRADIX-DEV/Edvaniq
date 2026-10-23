using System.Reflection;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Finds every entity of the model that belongs to a user (IOwnedByUser) and removes the user's rows of it, so a new entity
// is covered the moment it has an OwnerId. A row without an OwnerId goes with the row it hangs on, by cascade delete, so
// that relationship must be required: an optional one blocks the delete in the database and the message comes back. The
// rows are loaded and removed through the change tracker, so they leave with the SaveChanges that stores the inbox entry,
// not before. That suits the data of one user. A service that holds a lot per user removes it in batches of its own.
internal sealed partial class UserDataRemover<TContext>(TContext db, ILogger<UserDataRemover<TContext>> logger) : IUserDataRemover
    where TContext : ServiceDbContext
{
    private static readonly MethodInfo RemoveRowsOf = typeof(UserDataRemover<TContext>)
        .GetMethod(nameof(RemoveRows), BindingFlags.NonPublic | BindingFlags.Static)!;

    public int RemoveAll(string userId)
    {
        var rows = OwnedTypes(db.Model).Sum(type => (int)RemoveRowsOf.MakeGenericMethod(type).Invoke(null, [db, userId])!);
        LogRemoving(logger, rows);
        return rows;
    }

    // Each type that belongs to a user, once: a derived type is in the set of its base.
    internal static IEnumerable<Type> OwnedTypes(IModel model) =>
        model.GetEntityTypes()
            .Where(type => IsOwnedByUser(type) && (type.BaseType is null || !IsOwnedByUser(type.BaseType)))
            .Where(type => !type.IsOwned() && !type.HasSharedClrType)
            .Select(type => type.ClrType);

    private static bool IsOwnedByUser(IEntityType type) => typeof(IOwnedByUser).IsAssignableFrom(type.ClrType);

    // Every query filter off, not only Owner: a row a service hides, for example as archived, is the user's all the same.
    // Synchronous on purpose: MySql.Data's OpenAsync can hang for good (see MySqlHealthCheck).
    private static int RemoveRows<TEntity>(DbContext db, string userId) where TEntity : class
    {
        var rows = db.Set<TEntity>()
            .IgnoreQueryFilters()
            .Where(entity => EF.Property<string>(entity, nameof(IOwnedByUser.OwnerId)) == userId)
            .ToList();
        db.Set<TEntity>().RemoveRange(rows);
        return rows.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rows of a deleted user to remove: {Rows}")]
    private static partial void LogRemoving(ILogger logger, int rows);
}