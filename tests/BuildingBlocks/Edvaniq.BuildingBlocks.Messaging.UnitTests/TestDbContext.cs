using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// A service's DbContext without a service. Nothing here talks to a database unless a test says so.
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : ServiceDbContext(options, new NoUser())
{
    // Nothing listens on port 1, so a connection is refused at once.
    public const string UnreachableDatabase = "Server=127.0.0.1;Port=1;Database=unused;User ID=test";

    public static TestDbContext Create() => new(Options());

    public static DbContextOptions<TestDbContext> Options() =>
        new DbContextOptionsBuilder<TestDbContext>().UseMySQL(MySqlConnections.For(UnreachableDatabase).ConnectionString).Options;

    private sealed class NoUser : ICurrentUser
    {
        public string Id => throw new InvalidOperationException("There is no user.");
    }
}