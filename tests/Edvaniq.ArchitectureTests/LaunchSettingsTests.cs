using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// Every process gets its local ports from its launchSettings.json. The service template picks them at random, so a new
// service can take a port another one already has, and then both try to listen on it in the AppHost.
public class LaunchSettingsTests
{
    [Fact]
    public void Ports_AreUniqueAcrossProcesses()
    {
        var root = RepositoryRoot();
        var srcFolder = Path.Combine(root, "src");
        var launchSettingsFiles = Directory.EnumerateFiles(srcFolder, "launchSettings.json", SearchOption.AllDirectories);

        var ports = launchSettingsFiles.SelectMany(file =>
        {
            var content = File.ReadAllText(file);
            var matches = PortPattern.Matches(content);
            return matches.Select(match => new { Port = int.Parse(match.Groups[1].Value), File = file }).Distinct();
        }).ToList();

        var collisions = ports.GroupBy(p => p.Port)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToList();

        var message = "Port collisions found:\n"
            + string.Join("\n", collisions.Select(c => $"Port {c.Port} in {Path.GetRelativePath(root, c.File)}"));
        Assert.True(collisions.Count == 0, message);
    }

    private static readonly Regex PortPattern = new(@"localhost:(\d+)");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Edvaniq.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Edvaniq.slnx not found above " + AppContext.BaseDirectory);
    }
}
