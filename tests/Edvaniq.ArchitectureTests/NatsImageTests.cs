using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// The broker runs in the same NATS image in the AppHost and in the integration tests. The server has no NATS yet, once it
// does, its image in deploy/compose.yml joins this test like it joined the one for MySQL (MySqlImageTests).
public partial class NatsImageTests
{
    [Fact]
    public async Task AppHostAndTests_UseTheSameNatsImage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Edvaniq_AppHost>(cancellationToken);
        var annotation = builder.Resources.OfType<NatsServerResource>().Single().Annotations.OfType<ContainerImageAnnotation>().Single();
        var appHost = $"{annotation.Image.Replace("library/", "")}:{annotation.Tag}";

        var tests = TestImage().Match(File.ReadAllText(Path.Combine(Repository.Root, "tests", "Edvaniq.Testing", "NatsTestServer.cs"))).Groups[1].Value;

        Assert.StartsWith("nats:", tests);
        Assert.Equal(tests, appHost);
    }

    [GeneratedRegex(@"\bImage\s*=\s*""([^""]+)""")]
    private static partial Regex TestImage();
}