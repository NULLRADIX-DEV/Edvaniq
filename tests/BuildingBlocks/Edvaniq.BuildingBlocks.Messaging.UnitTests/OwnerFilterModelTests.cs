using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// EF only takes a filter on the root of a hierarchy. Built without a database.
public class OwnerFilterModelTests
{
    [Fact]
    public void DerivedTypeOfAnOwnedEntity_SharesTheFilterOfItsBase()
    {
        using var db = new EntriesDbContext();

        var entry = db.Model.FindEntityType(typeof(Entry))!;
        var archived = db.Model.FindEntityType(typeof(ArchivedEntry))!;

        Assert.NotNull(entry.FindDeclaredQueryFilter(ServiceDbContext.OwnerFilter));
        Assert.Same(entry, archived.BaseType);
        Assert.Null(archived.FindDeclaredQueryFilter(ServiceDbContext.OwnerFilter));
    }

    private sealed class EntriesDbContext() : ServiceDbContext(
        new DbContextOptionsBuilder<EntriesDbContext>().UseMySQL(MySqlConnections.For(TestDbContext.UnreachableDatabase).ConnectionString).Options,
        new NoUser())
    {
        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Entry>();
            modelBuilder.Entity<ArchivedEntry>();
        }
    }

    private class Entry : IOwnedByUser
    {
        public Guid Id { get; set; }

        public required string OwnerId { get; set; }
    }

    private sealed class ArchivedEntry : Entry;

    private sealed class NoUser : ICurrentUser
    {
        public string Id => throw new InvalidOperationException("There is no user.");
    }
}