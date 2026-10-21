using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Testing;

using MySql.Data.MySqlClient;

namespace Edvaniq.Services.Planning.IntegrationTests;

// The database user of a service may use its own database and nothing else, and MySQL itself refuses the rest. Shown
// with two databases that each have a user of their own, made the way MySqlTestServer makes them for every service,
// with the grant the AppHost runs.
public sealed class DatabaseUserTests(DatabaseUserTests.TwoDatabases databases) : IClassFixture<DatabaseUserTests.TwoDatabases>
{
    // MySQL error numbers: access denied to a database, to a table, and to an administrative statement. Not 1045,
    // that one also means a wrong password.
    private static readonly int[] AccessDenied = [1044, 1142, 1227];

    [Fact]
    public void OwnDatabase_CanBeReadAndWritten()
    {
        Execute(databases.Own, "INSERT INTO notes (memo) VALUES ('mine')");

        Assert.Equal(["mine"], Query(databases.Own, "SELECT memo FROM notes"));
    }

    [Fact]
    public void ForeignDatabase_CannotBeRead()
    {
        var foreign = databases.ForeignName;

        AssertDenied(() => Query(databases.Own, $"SELECT memo FROM `{foreign}`.secrets"));
        AssertDenied(() => Query(databases.Own, $"SHOW TABLES FROM `{foreign}`"));
        AssertDenied(() => Query(databases.Own, "SELECT user FROM mysql.user"));
        AssertDenied(() => Execute(databases.Own, $"USE `{foreign}`"));
    }

    [Fact]
    public void ForeignDatabase_CannotBeChanged()
    {
        var foreign = databases.ForeignName;

        AssertDenied(() => Execute(databases.Own, $"INSERT INTO `{foreign}`.secrets (memo) VALUES ('mine')"));
        AssertDenied(() => Execute(databases.Own, $"CREATE TABLE `{foreign}`.extra (memo VARCHAR(20))"));
        AssertDenied(() => Execute(databases.Own, $"DROP TABLE `{foreign}`.secrets"));
        AssertDenied(() => Execute(databases.Own, $"DROP DATABASE `{foreign}`"));

        Assert.Equal(["secret"], Query(databases.Foreign, "SELECT memo FROM secrets"));
    }

    [Fact]
    public void Server_CannotCreateDatabasesUsersOrRights()
    {
        AssertDenied(() => Execute(databases.Own, "CREATE DATABASE isolationintruder"));
        AssertDenied(() => Execute(databases.Own, "CREATE USER 'isolationintruder'@'%'"));
        AssertDenied(() => Execute(databases.Own, $"GRANT ALL PRIVILEGES ON `{databases.ForeignName}`.* TO CURRENT_USER()"));
    }

    [Fact]
    public void ForeignDatabase_IsNeitherListedNorVisibleInTheSchema()
    {
        // The system schemas are visible to every user and hold nothing of the others.
        string[] system = ["information_schema", "performance_schema"];
        var listed = Query(databases.Own, "SHOW DATABASES").Except(system);

        Assert.Equal([databases.OwnName], listed);
        Assert.Equal(["notes"], Query(databases.Own, TablesOf(databases.OwnName)));
        Assert.Empty(Query(databases.Own, TablesOf(databases.ForeignName)));
    }

    private static string TablesOf(string database) =>
        $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{database}'";

    private static void AssertDenied(Action action)
    {
        var exception = Assert.Throws<MySqlException>(action);

        Assert.Contains(exception.Number, AccessDenied);
    }

    // Synchronous on purpose: MySql.Data's OpenAsync can hang for good (see MySqlHealthCheck).
    private static MySqlConnection Open(string connectionString)
    {
        var connection = new MySqlConnection(MySqlConnections.For(connectionString).ConnectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = Open(connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> Query(string connectionString, string sql)
    {
        using var connection = Open(connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    // Both databases are made once for the class. Each user creates one table in its own database, which shows that
    // the logins work and that the refusals above are not a broken login.
    public sealed class TwoDatabases(MySqlTestServer mysql) : IAsyncLifetime
    {
        public string Own { get; private set; } = "";

        public string Foreign { get; private set; } = "";

        public string OwnName => MySqlConnections.For(Own).Database;

        public string ForeignName => MySqlConnections.For(Foreign).Database;

        public async ValueTask InitializeAsync()
        {
            Own = await mysql.CreateDatabaseAsync("own");
            Foreign = await mysql.CreateDatabaseAsync("foreign");

            Execute(Own, "CREATE TABLE notes (memo VARCHAR(20) NOT NULL)");
            Execute(Foreign, "CREATE TABLE secrets (memo VARCHAR(20) NOT NULL)");
            Execute(Foreign, "INSERT INTO secrets (memo) VALUES ('secret')");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}