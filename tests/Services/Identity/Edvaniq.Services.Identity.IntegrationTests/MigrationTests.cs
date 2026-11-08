using Edvaniq.Services.Identity.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// The migrations fit the model and apply to a fresh database, a second run changes nothing.
public sealed class MigrationTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    // A model change without its migration fails here and not only during the deploy. Needs no database.
    [Fact]
    public void Migrations_MatchTheModel()
    {
        using var context = new IdentityDbContextFactory().CreateDbContext([]);

        Assert.NotEmpty(context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges(), "The model has changed without a migration (dotnet ef migrations add).");
    }

    // IdentityDatabase migrated a fresh database before the first test.
    [Fact]
    public async Task Migrate_OnFreshDatabase_AppliesAllMigrations()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database;

        Assert.Equal(db.GetMigrations(), db.GetAppliedMigrations());
    }

    [Fact]
    public async Task Migrate_RunAgain_ChangesNothing()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database;
        var applied = db.GetAppliedMigrations().ToList();

        db.Migrate();

        Assert.Equal(applied, db.GetAppliedMigrations());
        Assert.Empty(db.GetPendingMigrations());
    }
}