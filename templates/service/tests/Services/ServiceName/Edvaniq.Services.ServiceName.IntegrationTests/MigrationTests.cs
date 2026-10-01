using Edvaniq.Services.ServiceName.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

// A model change without its migration fails here and not only during the deploy. Needs no database.
public sealed class MigrationTests
{
    [Fact]
    public void Migrations_MatchTheModel()
    {
        using var context = new ServiceNameDbContextFactory().CreateDbContext([]);

        Assert.NotEmpty(context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges(), "The model has changed without a migration (dotnet ef migrations add).");
    }
}
