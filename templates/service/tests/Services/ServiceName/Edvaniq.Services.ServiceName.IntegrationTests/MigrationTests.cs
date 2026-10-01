using Edvaniq.Services.ServiceName.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

// The migrations fit the model and apply to a fresh database, a second run changes nothing.
public sealed class MigrationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    // A model change without its migration fails here and not only during the deploy. Needs no database.
    [Fact]
    public void Migrations_MatchTheModel()
    {
        using var context = new ServiceNameDbContextFactory().CreateDbContext([]);

        Assert.NotEmpty(context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges(), "The model has changed without a migration (dotnet ef migrations add).");
    }

    // ServiceFactory migrated a fresh database before the first test.
    [Fact]
    public void Migrate_OnFreshDatabase_AppliesAllMigrations()
    {
        using var scope = service.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ServiceNameDbContext>().Database;

        Assert.Equal(database.GetMigrations(), database.GetAppliedMigrations());
    }

    [Fact]
    public void Migrate_RunAgain_ChangesNothing()
    {
        using var scope = service.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ServiceNameDbContext>().Database;
        var applied = database.GetAppliedMigrations().ToList();

        database.Migrate();

        Assert.Equal(applied, database.GetAppliedMigrations());
        Assert.Empty(database.GetPendingMigrations());
    }
}
