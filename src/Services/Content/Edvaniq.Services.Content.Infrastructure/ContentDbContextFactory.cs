using Edvaniq.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Edvaniq.Services.Content.Infrastructure;

// Only for dotnet ef and the migration test. Adding a migration needs no database, so a placeholder is enough.
public sealed class ContentDbContextFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
    public ContentDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<ContentDbContext>()
                .UseMySQL($"Server=localhost;Database={InfrastructureExtensions.DatabaseName}")
                .Options,
            new NoUser());

    // dotnet ef builds the model but never queries user data.
    private sealed class NoUser : ICurrentUser
    {
        public string Id => throw new InvalidOperationException("There is no user at design time.");
    }
}
