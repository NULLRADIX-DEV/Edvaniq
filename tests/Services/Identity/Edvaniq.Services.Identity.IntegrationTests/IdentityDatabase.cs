using Edvaniq.Services.Identity.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// One MySQL container for all tests of this project. Every IdentityDatabase creates its own database in it.
[assembly: AssemblyFixture(typeof(MySqlTestServer))]

namespace Edvaniq.Services.Identity.IntegrationTests;

// A fresh database with all migrations applied, one per test class (IClassFixture). Pass ConnectionString to an
// IdentityFactory.
public sealed class IdentityDatabase(MySqlTestServer mysql) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await mysql.CreateDatabaseAsync(InfrastructureExtensions.DatabaseName);

        // The same synchronous call the migrate step makes.
        await using var identity = new IdentityFactory(connectionString: ConnectionString);
        using var scope = identity.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.Migrate();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}