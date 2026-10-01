using System.Data.Common;
using Testcontainers.MySql;
using Xunit;

namespace Edvaniq.Testing;

// One MySQL container per test run, in the version the AppHost uses. Every test class gets a fresh database of its own
// (CreateDatabaseAsync), so tests see neither each other's data nor depend on their order. Like on the server, the
// service connects with a user of its own that may only use that database, never as root. Needs Docker, like the
// AppHost. Register it once per test project: [assembly: AssemblyFixture(typeof(MySqlTestServer))].
public sealed class MySqlTestServer : IAsyncLifetime
{
    // Same image as the AppHost (Aspire.Hosting.MySql).
    private const string Image = "mysql:9.7";

    private readonly MySqlContainer container = new MySqlBuilder(Image).WithUsername("root").Build();
    private int databaseCount;

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public ValueTask DisposeAsync() => container.DisposeAsync();

    // Creates an empty database and a user of the same name with rights on it only, and returns the connection string
    // of that user.
    public async Task<string> CreateDatabaseAsync(string namePrefix)
    {
        var name = $"{namePrefix}{Interlocked.Increment(ref databaseCount)}";
        var password = Guid.NewGuid().ToString("N");

        // The mysql client in the container, as root. ExecScriptAsync builds a broken call for the root user.
        var sql = $"CREATE DATABASE `{name}`; CREATE USER \"{name}\"@\"%\" IDENTIFIED BY \"{password}\"; "
            + $"GRANT ALL PRIVILEGES ON `{name}`.* TO \"{name}\"@\"%\";";
        var result = await container.ExecAsync(["sh", "-c", $"MYSQL_PWD=\"$MYSQL_ROOT_PASSWORD\" mysql -uroot -e '{sql}'"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating database {name} failed: {result.Stderr}");
        }

        var connectionString = new DbConnectionStringBuilder { ConnectionString = container.GetConnectionString() };
        connectionString["Database"] = name;
        connectionString["Uid"] = name;
        connectionString["Pwd"] = password;
        return connectionString.ConnectionString;
    }
}
