using Edvaniq.Services.TestService.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.TestService.IntegrationTests;

// The migrations fit the model and apply to a fresh database, a second run changes nothing.
public sealed class MigrationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    // A model change without its migration fails here and not only during the deploy. Needs no database.
    [Fact]
    public void Migrations_MatchTheModel()
    {
        using var context = new TestServiceDbContextFactory().CreateDbContext([]);

        Assert.NotEmpty(context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges(), "The model has changed without a migration (dotnet ef migrations add).");
    }

    // ServiceFactory migrated a fresh database before the first test.
    [Fact]
    public void Migrate_OnFreshDatabase_AppliesAllMigrations()
    {
        using var scope = service.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestServiceDbContext>().Database;

        Assert.Equal(database.GetMigrations(), database.GetAppliedMigrations());
    }

    // The provider leaves the collation out of ALTER TABLE, so a column that existed before gets it from the migration by hand.
    [Fact]
    public void OwnerIdColumns_AreComparedByteForByte()
    {
        using var scope = service.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<TestServiceDbContext>().Database.GetDbConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME, COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME = 'OwnerId'";
        using var reader = command.ExecuteReader();
        var columns = new List<(string Table, string? Collation)>();
        while (reader.Read())
        {
            columns.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        Assert.NotEmpty(columns);
        Assert.All(columns, column => Assert.True(column.Collation == "utf8mb4_0900_bin", $"{column.Table}.OwnerId has the collation {column.Collation}."));
    }

    [Fact]
    public void Migrate_RunAgain_ChangesNothing()
    {
        using var scope = service.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestServiceDbContext>().Database;
        var applied = database.GetAppliedMigrations().ToList();

        database.Migrate();

        Assert.Equal(applied, database.GetAppliedMigrations());
        Assert.Empty(database.GetPendingMigrations());
    }
}
