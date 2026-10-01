using System.Data.Common;
using Testcontainers.MySql;
using Xunit;

namespace Edvaniq.Testing;

// One MySQL container per test run, in the version the AppHost uses. Every test class gets a fresh database of its own
// (CreateDatabaseAsync), so tests see neither each other's data nor depend on their order. Needs Docker, like the
// AppHost. Register it once per test project: [assembly: AssemblyFixture(typeof(MySqlTestServer))].
public sealed class MySqlTestServer : IAsyncLifetime
{
    // Same image as the AppHost (Aspire.Hosting.MySql).
    private const string Image = "mysql:9.7";

    private readonly MySqlContainer container = new MySqlBuilder(Image).WithUsername("root").Build();
    private int databaseCount;

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public ValueTask DisposeAsync() => container.DisposeAsync();

    // Creates an empty database and returns its connection string.
    public async Task<string> CreateDatabaseAsync(string namePrefix)
    {
        var name = $"{namePrefix}{Interlocked.Increment(ref databaseCount)}";

        // The mysql client in the container, as root. ExecScriptAsync builds a broken call for the root user.
        var result = await container.ExecAsync(
            ["sh", "-c", $"MYSQL_PWD=\"$MYSQL_ROOT_PASSWORD\" mysql -uroot -e 'CREATE DATABASE `{name}`'"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating database {name} failed: {result.Stderr}");
        }

        var connectionString = new DbConnectionStringBuilder { ConnectionString = container.GetConnectionString() };
        connectionString["Database"] = name;
        return connectionString.ConnectionString;
    }
}
