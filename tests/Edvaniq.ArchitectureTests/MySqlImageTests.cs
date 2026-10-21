using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// The database runs in the same MySQL image everywhere: locally in the AppHost, in the integration tests and on the
// server. Dependabot raises the image in deploy/compose.yml only, so this test turns its pull request red until the
// tests and the AppHost follow in the same pull request.
public partial class MySqlImageTests
{
    [Fact]
    public async Task AppHostTestsAndServer_UseTheSameMySqlImage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Edvaniq_AppHost>(cancellationToken);
        var annotation = builder.Resources.OfType<MySqlServerResource>().Single().Annotations.OfType<ContainerImageAnnotation>().Single();
        var appHost = $"{annotation.Image.Replace("library/", "")}:{annotation.Tag}";

        var server = ComposeImage().Match(File.ReadAllText(Path.Combine(Repository.Root, "deploy", "compose.yml"))).Groups[1].Value;
        var tests = TestImage().Match(File.ReadAllText(Path.Combine(Repository.Root, "tests", "Edvaniq.Testing", "MySqlTestServer.cs"))).Groups[1].Value;

        Assert.StartsWith("mysql:", server);
        Assert.Equal(server, tests);
        Assert.Equal(server, appHost);
    }

    [GeneratedRegex(@"^\s*image:\s*(mysql:\S+)", RegexOptions.Multiline)]
    private static partial Regex ComposeImage();

    [GeneratedRegex(@"\bImage\s*=\s*""([^""]+)""")]
    private static partial Regex TestImage();
}