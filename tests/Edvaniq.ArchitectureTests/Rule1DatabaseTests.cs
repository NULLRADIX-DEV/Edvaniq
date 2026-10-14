namespace Edvaniq.ArchitectureTests;

// Rule 1 in docs/architecture.md: every process gets at most one database, and only the one of its own service. Locally
// the AppHost hands it over as ConnectionStrings__<service>db. The test builds the AppHost's model without starting it,
// so it needs no Docker. On the server, the CI checks the env files in deploy/compose.yml.
public class Rule1DatabaseTests
{
    private const string ConnectionString = "ConnectionStrings__";

    [Fact]
    public async Task EveryProcessInTheAppHost_GetsOnlyTheDatabaseOfItsService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Edvaniq_AppHost>(cancellationToken);
        var databases = builder.Resources.OfType<MySqlDatabaseResource>().Select(database => database.Name).ToHashSet();
        var servers = builder.Resources.OfType<MySqlServerResource>().Select(server => server.Name).ToHashSet();
        await using var app = await builder.BuildAsync(cancellationToken);
        var executionContext = app.Services.GetRequiredService<DistributedApplicationExecutionContext>();

        var processes = new List<DatabaseRule.Process>();
        foreach (var project in builder.Resources.OfType<ProjectResource>())
        {
            var variables = new Dictionary<string, object>();
            var context = new EnvironmentCallbackContext(executionContext, project, variables, cancellationToken);
            foreach (var annotation in project.Annotations.OfType<EnvironmentCallbackAnnotation>())
            {
                await annotation.Callback(context);
            }

            var connections = variables.Keys
                .Where(name => name.StartsWith(ConnectionString, StringComparison.Ordinal))
                .Select(name => name[ConnectionString.Length..])
                .ToList();
            processes.Add(new DatabaseRule.Process(project.Name, connections));
        }

        Rules.AssertNone(DatabaseRule.Violations(processes, databases, servers), "Rule 1");
        Assert.Contains(processes, process => process.Name == "planning-api" && process.Connections.Contains("planningdb"));
    }

    [Fact]
    public void ProcessesWithTheirOwnDatabase_AreNotReported()
    {
        DatabaseRule.Process[] processes =
        [
            new("planning-api", ["planningdb"]),
            new("planning-migrate", ["planningdb"]),
            new("content-worker", ["contentdb"]),
            new("gateway", ["something-else"]),
            new("web", []),
        ];

        Assert.Empty(DatabaseRule.Violations(processes, Databases, Servers));
    }

    [Fact]
    public void ProcessWithTheDatabaseOfAnotherService_IsReported()
    {
        DatabaseRule.Process[] processes = [new("planning-api", ["contentdb"])];

        var violations = DatabaseRule.Violations(processes, Databases, Servers);

        Assert.Equal(["planning-api gets contentdb, the database of content"], violations);
    }

    [Fact]
    public void ProcessOfAServiceWithASimilarName_IsReported()
    {
        DatabaseRule.Process[] processes = [new("planningengine-api", ["planningdb"])];

        var violations = DatabaseRule.Violations(processes, Databases, Servers);

        Assert.Equal(["planningengine-api gets planningdb, the database of planning"], violations);
    }

    [Fact]
    public void ProcessWithTwoDatabases_IsReported()
    {
        DatabaseRule.Process[] processes = [new("content-worker", ["contentdb", "planningdb"])];

        var violations = DatabaseRule.Violations(processes, Databases, Servers);

        Assert.Contains("content-worker gets 2 databases: contentdb, planningdb", violations);
    }

    [Fact]
    public void ProcessWithTheWholeServer_IsReported()
    {
        DatabaseRule.Process[] processes = [new("gateway", ["mysql"])];

        var violations = DatabaseRule.Violations(processes, Databases, Servers);

        Assert.Equal(["gateway gets the database server mysql"], violations);
    }

    private static readonly HashSet<string> Databases = ["planningdb", "contentdb"];

    private static readonly HashSet<string> Servers = ["mysql"];
}