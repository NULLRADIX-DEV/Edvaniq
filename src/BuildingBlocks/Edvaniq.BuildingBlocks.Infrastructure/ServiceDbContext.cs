using System.Linq.Expressions;
using System.Reflection;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.BuildingBlocks.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Base of every service's DbContext. Each entity that belongs to a user (IOwnedByUser) gets the query filter "Owner":
// queries only see the rows of the user from the token, so user B can neither read, change nor delete what belongs to
// A. Code that must see all users says so with IgnoreQueryFilters([OwnerFilter]), the UserDataRemover drops every
// filter. It also maps
// the outbox and inbox tables, so no service can forget them.
public abstract class ServiceDbContext(DbContextOptions options, ICurrentUser currentUser) : DbContext(options)
{
    public const string OwnerFilter = "Owner";

    // A user id from the issuer. Bounded so MySQL can index it.
    public const int OwnerIdMaxLength = 128;

    // Compared byte for byte, so "Abc" and "abc" are two users. The default utf8mb4_0900_ai_ci would merge them.
    // The MySQL provider ignores UseCollation on a column, only this annotation reaches the SQL, and only for new
    // tables. A column that already exists needs an ALTER TABLE by hand in the migration.
    public const string OwnerIdCollation = "utf8mb4_0900_bin";

    public const string CollationAnnotation = "MySQL:Collation";

    // Read for every query. Without a user it throws, so a query outside a request fails instead of seeing everyone.
    private string CurrentUserId => currentUser.Id;

    // Sealed, so a service cannot skip the filter by overriding it. Services configure their model in ConfigureModel.
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);
        modelBuilder.AddMessagingTables();

        // EF only takes a filter on the root, derived types get it from their base.
        var owned = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => IsOwnedByUser(entityType.ClrType)
                && (entityType.BaseType is null || !IsOwnedByUser(entityType.BaseType.ClrType)))
            .ToList();
        foreach (var entityType in owned)
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.Property(nameof(IOwnedByUser.OwnerId))
                .IsRequired()
                .HasMaxLength(OwnerIdMaxLength)
                .HasAnnotation(CollationAnnotation, OwnerIdCollation);
            entity.HasIndex(nameof(IOwnedByUser.OwnerId));
            entity.HasQueryFilter(OwnerFilter, OwnerIsCurrentUser(entityType.ClrType));
        }
    }

    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }

    private static bool IsOwnedByUser(Type type) => typeof(IOwnedByUser).IsAssignableFrom(type);

    // e => e.OwnerId == this.CurrentUserId. EF swaps "this" for the context that runs the query.
    private LambdaExpression OwnerIsCurrentUser(Type entityType)
    {
        var entity = Expression.Parameter(entityType, "e");
        var currentUserId = typeof(ServiceDbContext).GetProperty(nameof(CurrentUserId), BindingFlags.Instance | BindingFlags.NonPublic)!;

        return Expression.Lambda(
            Expression.Equal(
                Expression.Property(entity, nameof(IOwnedByUser.OwnerId)),
                Expression.Property(Expression.Constant(this), currentUserId)),
            entity);
    }
}