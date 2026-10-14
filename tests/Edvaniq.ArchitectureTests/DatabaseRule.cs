namespace Edvaniq.ArchitectureTests;

// Rule 1 in docs/architecture.md: a process gets at most one database, and only the one of its own service. The database
// of service <service> is <service>db, and a process belongs to the service its name starts with (planning-api,
// planning-migrate). Connections are the names of the connection strings a process gets, databases and servers the
// names of the MySQL resources in the AppHost.
internal static class DatabaseRule
{
    public sealed record Process(string Name, IReadOnlyCollection<string> Connections);

    public static IEnumerable<string> Violations(
        IEnumerable<Process> processes, IReadOnlySet<string> databases, IReadOnlySet<string> servers)
    {
        foreach (var process in processes)
        {
            foreach (var server in process.Connections.Where(servers.Contains))
            {
                yield return $"{process.Name} gets the database server {server}";
            }

            var own = process.Connections.Where(databases.Contains).ToList();
            if (own.Count > 1)
            {
                yield return $"{process.Name} gets {own.Count} databases: {string.Join(", ", own)}";
            }

            foreach (var database in own)
            {
                var service = database.EndsWith("db", StringComparison.Ordinal) ? database[..^2] : database;
                if (process.Name != service && !process.Name.StartsWith(service + "-", StringComparison.Ordinal))
                {
                    yield return $"{process.Name} gets {database}, the database of {service}";
                }
            }
        }
    }
}