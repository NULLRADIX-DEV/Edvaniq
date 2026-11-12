using Microsoft.Extensions.Configuration;

namespace Edvaniq.Gateway.IntegrationTests;

// On the server the gateway finds a service only through services__<name>__http__0 in deploy/compose.yml. A cluster
// without that line would answer 502 for good.
public sealed class ServerAddressTests
{
    private const string Prefix = "https+http://";

    [Fact]
    public void EveryDestination_HasAnAddressInCompose()
    {
        var root = RepositoryRoot();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "Gateway", "Edvaniq.Gateway", "appsettings.json"))
            .Build();
        var addresses = configuration.GetSection("ReverseProxy:Clusters").GetChildren()
            .SelectMany(cluster => cluster.GetSection("Destinations").GetChildren())
            .Select(destination => destination["Address"])
            .ToList();
        var gateway = GatewayServiceOfCompose(root);

        Assert.True(addresses.Count >= 6, "Expected a destination per active service and Identity.");
        Assert.All(addresses, address =>
        {
            Assert.StartsWith(Prefix, address);
            var name = address![Prefix.Length..];
            Assert.Contains($"services__{name}__http__0: http://{name}:8080", gateway);
        });
    }

    // The same for one service asking another: every WithReference of a service or worker in AppHost.cs needs its line
    // in that process's service in deploy/compose.yml, like content-worker asking Planning (#323). The gateway is the
    // test above, the web host only reaches the gateway.
    [Fact]
    public void EveryServiceAnotherAsks_HasAnAddressInCompose()
    {
        var root = RepositoryRoot();
        var appHost = File.ReadAllText(Path.Combine(root, "src", "Aspire", "Edvaniq.AppHost", "AppHost.cs"));
        var resources = System.Text.RegularExpressions.Regex.Matches(appHost, @"var (\w+) = (?:AddService<[^>]+>|builder\.AddProject<[^>]+>)\(""([\w-]+)""")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        var references = new List<(string Process, string Target)>();
        foreach (var statement in appHost.Split(';'))
        {
            var process = System.Text.RegularExpressions.Regex.Match(statement, @"(?:AddService<[^>]+>|builder\.AddProject<[^>]+>)\(""([\w-]+)""") is { Success: true } added
                ? added.Groups[1].Value
                : System.Text.RegularExpressions.Regex.Match(statement, @"^\s*(?://[^\n]*\n\s*)*(\w+)\.WithReference") is { Success: true } named && resources.TryGetValue(named.Groups[1].Value, out var resource)
                    ? resource
                    : null;
            if (process is null or "gateway" or "web")
            {
                continue;
            }

            references.AddRange(System.Text.RegularExpressions.Regex.Matches(statement, @"\.WithReference\((\w+)\)")
                .Select(match => match.Groups[1].Value)
                .Where(resources.ContainsKey)
                .Select(variable => (process, resources[variable])));
        }

        Assert.Contains(("content-worker", "planning-api"), references);
        Assert.All(references, reference => Assert.Contains(
            $"services__{reference.Target}__http__0: http://{reference.Target}:8080", ServiceOfCompose(root, reference.Process)));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Edvaniq.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException("Edvaniq.slnx not found.");
        }

        return directory.FullName;
    }

    private static IReadOnlyList<string> GatewayServiceOfCompose(string root) => ServiceOfCompose(root, "gateway");

    // The lines of a service, up to the next service.
    private static IReadOnlyList<string> ServiceOfCompose(string root, string service)
    {
        var lines = File.ReadAllLines(Path.Combine(root, "deploy", "compose.yml"));
        var start = Array.IndexOf(lines, $"  {service}:");
        Assert.True(start >= 0, $"deploy/compose.yml has no service {service}.");
        return lines.Skip(start + 1)
            .TakeWhile(line => line.StartsWith("   ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToList();
    }
}