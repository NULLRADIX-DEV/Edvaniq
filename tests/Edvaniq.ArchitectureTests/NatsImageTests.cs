using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// The broker runs in the same NATS image everywhere: locally in the AppHost, in the integration tests and on the server.
// Dependabot raises the image in deploy/compose.yml only, so this test turns its pull request red until the tests and the
// AppHost follow in the same pull request. Only the server pins the digest (ComposeImageTests), so the comparison stops
// at the tag.
public partial class NatsImageTests
{
    [Fact]
    public async Task AppHostTestsAndServer_UseTheSameNatsImage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Edvaniq_AppHost>(cancellationToken);
        var annotation = builder.Resources.OfType<NatsServerResource>().Single().Annotations.OfType<ContainerImageAnnotation>().Single();
        var appHost = $"{annotation.Image.Replace("library/", "")}:{annotation.Tag}";

        var server = ComposeImage().Match(File.ReadAllText(Path.Combine(Repository.Root, "deploy", "compose.yml"))).Groups[1].Value;
        var tests = TestImage().Match(File.ReadAllText(Path.Combine(Repository.Root, "tests", "Edvaniq.Testing", "NatsTestServer.cs"))).Groups[1].Value;

        Assert.StartsWith("nats:", server);
        Assert.Equal(server, tests);
        Assert.Equal(server, appHost);
    }

    [GeneratedRegex(@"^\s*image:\s*(nats:[^\s@]+)", RegexOptions.Multiline)]
    private static partial Regex ComposeImage();

    [GeneratedRegex(@"\bImage\s*=\s*""([^""]+)""")]
    private static partial Regex TestImage();
}