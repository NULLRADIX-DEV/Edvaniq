using System.Linq.Expressions;
using System.Reflection;
using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Base of every service's DbContext. Each entity that belongs to a user (IOwnedByUser) gets the query filter "Owner":
// queries only see the rows of the user from the token, so user B can neither read, change nor delete what belongs to
// A. Code that must see all users, like the delete consumer, says so with IgnoreQueryFilters([OwnerFilter]).
public abstract class ServiceDbContext(DbContextOptions options, ICurrentUser currentUser) : DbContext(options)
{
    public const string OwnerFilter = "Owner";

    // A user id from the issuer. Bounded so MySQL can index it.
    public const int OwnerIdMaxLength = 128;

    // Read for every query. Without a user it throws, so a query outside a request fails instead of seeing everyone.
    private string CurrentUserId => currentUser.Id;

    // Sealed, so a service cannot skip the filter by overriding it. Services configure their model in ConfigureModel.
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);

        var owned = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => typeof(IOwnedByUser).IsAssignableFrom(entityType.ClrType))
            .ToList();
        foreach (var entityType in owned)
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.Property(nameof(IOwnedByUser.OwnerId)).IsRequired().HasMaxLength(OwnerIdMaxLength);
            entity.HasIndex(nameof(IOwnedByUser.OwnerId));
            entity.HasQueryFilter(OwnerFilter, OwnerIsCurrentUser(entityType.ClrType));
        }
    }

    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }

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
