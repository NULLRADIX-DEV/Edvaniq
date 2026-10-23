using System.Reflection;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Covers every IOwnedByUser type, so a new entity needs no extra code. Rows without OwnerId go by cascade delete, which
// needs a required relationship (an optional one blocks the delete). Goes through the change tracker, so the rows leave
// with the consumer's SaveChanges and its inbox entry. Fine for one user's data, a service with lots of it batches.
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

    // A derived type goes with its base.
    internal static IEnumerable<Type> OwnedTypes(IModel model) =>
        model.GetEntityTypes()
            .Where(type => IsOwnedByUser(type) && (type.BaseType is null || !IsOwnedByUser(type.BaseType)))
            .Where(type => !type.IsOwned() && !type.HasSharedClrType)
            .Select(type => type.ClrType);

    private static bool IsOwnedByUser(IEntityType type) => typeof(IOwnedByUser).IsAssignableFrom(type.ClrType);

    // All filters off, a hidden row is the user's too. Sync on purpose, MySql.Data's OpenAsync can hang (MySqlHealthCheck).
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