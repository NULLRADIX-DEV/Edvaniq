using System.Text.RegularExpressions;

namespace Edvaniq.ArchitectureTests;

// One deploy brings up exactly one state: every own image carries the commit of the release, and every other image is
// pinned by digest, so deploying the same commit again on a fresh server pulls the very same images. The CI job
// "Container images" checks that the own images match the processes in the AppHost.
public partial class ComposeImageTests
{
    [Fact]
    public void EveryImage_IsTheReleaseCommitOrPinnedByDigest()
    {
        var images = ImageLine().Matches(File.ReadAllText(Path.Combine(Repository.Root, "deploy", "compose.yml")))
            .Select(match => match.Groups[1].Value.Trim('"'))
            .ToList();

        Assert.Contains(images, image => OwnImage().IsMatch(image));
        Assert.Contains(images, image => PinnedImage().IsMatch(image));
        Assert.DoesNotContain(images, image => !IsReproducible(image));
    }

    [Theory]
    [InlineData("ghcr.io/nullradix-dev/edvaniq/web:sha-${APP_COMMIT:?}", true)]
    [InlineData("mysql:9.7@sha256:df90258da46f6e7ff8574ff84f17618aba0765cac749928299c69fd4e7cb8ad8", true)]
    [InlineData("ghcr.io/nullradix-dev/edvaniq/web:latest", false)]
    [InlineData("ghcr.io/nullradix-dev/edvaniq/web:sha-12ab162", false)]
    [InlineData("mysql:9.7", false)]
    [InlineData("mysql@sha256:abc", false)]
    public void IsReproducible_AcceptsOnlyTheCommitTagOrADigest(string image, bool expected) =>
        Assert.Equal(expected, IsReproducible(image));

    private static bool IsReproducible(string image) => OwnImage().IsMatch(image) || PinnedImage().IsMatch(image);

    [GeneratedRegex(@"^\s*image:\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"^ghcr\.io/nullradix-dev/edvaniq/[a-z0-9-]+:sha-\$\{APP_COMMIT:\?\}$")]
    private static partial Regex OwnImage();

    [GeneratedRegex(@"^[a-z0-9./-]+:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$")]
    private static partial Regex PinnedImage();
}