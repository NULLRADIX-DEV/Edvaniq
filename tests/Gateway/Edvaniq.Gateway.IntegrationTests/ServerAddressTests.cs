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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Edvaniq.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException("Edvaniq.slnx not found.");
        }

        return directory.FullName;
    }

    // The lines of the service gateway, up to the next service.
    private static IReadOnlyList<string> GatewayServiceOfCompose(string root)
    {
        var lines = File.ReadAllLines(Path.Combine(root, "deploy", "compose.yml"));
        var start = Array.IndexOf(lines, "  gateway:");
        Assert.True(start >= 0, "deploy/compose.yml has no service gateway.");
        return lines.Skip(start + 1)
            .TakeWhile(line => line.StartsWith("   ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToList();
    }
}