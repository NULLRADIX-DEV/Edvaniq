using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// The filter sits on the root of a hierarchy and OwnerId is compared byte for byte. Built without a database.
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

    // The MySQL provider drops a collation it does not know from the SQL without a word, so look at the SQL itself.
    [Fact]
    public void OwnerId_IsComparedByteForByte_InTheSqlOfANewTable()
    {
        using var db = new EntriesDbContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());

        var sql = string.Join(Environment.NewLine, db.GetService<IMigrationsSqlGenerator>().Generate(operations, model).Select(command => command.CommandText));

        Assert.Contains("`OwnerId` varchar(128) COLLATE utf8mb4_0900_bin NOT NULL", sql);
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