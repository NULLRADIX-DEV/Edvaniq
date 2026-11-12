extern alias referencerun;

using Microsoft.Extensions.Configuration;

using Run = referencerun::Edvaniq.Services.Content.ReferenceRun.ReferenceRun;

namespace Edvaniq.Services.Content.IntegrationTests;

// #309: the reference run, smoke run with the simulated provider at no cost. The real run is done by hand
// (tests/ReferenceSet/README.md). The simulated provider reads only headings and misses the history text on purpose,
// so the run ends below the minimum: the table is written anyway and names why.
public sealed class ReferenceRunTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"reference-run-{Guid.NewGuid():N}.md");

    [Fact]
    public async Task SimulatedRun_WritesTheTable_WithNamesAndNumbersOnly()
    {
        using var output = new StringWriter();

        var exit = await Run.RunAsync(["--runs", "2", "--out", file], Simulated, output, TestContext.Current.CancellationToken);

        Assert.Equal(Run.BelowMinimum, exit);
        Assert.StartsWith("Reference run: provider Simulated, prompt topics-", output.ToString(), StringComparison.Ordinal);
        var report = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        Assert.Contains("| biologie-fotosynthese | 14/14 | 1.00 |", report, StringComparison.Ordinal);
        Assert.Contains("| geschichte-industrialisierung | 0/14 | 0.00 |", report, StringComparison.Ordinal);
        Assert.Contains("verfehlt: Trefferquote, erfundene Themen", report, StringComparison.Ordinal);
        Assert.DoesNotContain("scan-ohne-textebene", report, StringComparison.Ordinal);

        // No answer of the AI, only topic names: nothing of its JSON.
        Assert.DoesNotContain("{", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\"topics\"", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--runs", "0")]
    [InlineData("--max-cost", "-1")]
    [InlineData("--unknown", "x")]
    [InlineData("--runs")]
    public async Task WrongArguments_StopBeforeAnything(params string[] args)
    {
        using var output = new StringWriter();

        Assert.Equal(Run.Stopped, await Run.RunAsync(args, Simulated, output, TestContext.Current.CancellationToken));
        Assert.StartsWith("Usage:", output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => File.Delete(file);

    private static void Simulated(IConfigurationBuilder configuration) =>
        configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)"Simulated")]);
}