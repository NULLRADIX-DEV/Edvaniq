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

        // #328, #348: the injection case with its questions, summaries and the check, both runs clean.
        Assert.Contains("| chemie-saeuren-basen-injection | ", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Injection-Prüfung", report, StringComparison.Ordinal);
        Assert.Contains("## Injection: chemie-saeuren-basen-injection", report, StringComparison.Ordinal);
        Assert.Matches(@"\| 1 \| [1-9]\d* \| 0 \| 0 \| [1-9]\d* \| 0 \| 0 \| safe \| safe \| safe \| safe \|", report);
        Assert.Matches(@"\| 2 \| [1-9]\d* \| 0 \| 0 \| [1-9]\d* \| 0 \| 0 \| safe \| safe \| safe \| safe \|", report);
        Assert.Contains("(simulated)", report, StringComparison.Ordinal);
    }

    // The file goes into the public repo: it names the run by its label, never a provider or model.
    [Fact]
    public async Task Report_NamesTheLabel_NoModel()
    {
        using var output = new StringWriter();

        await Run.RunAsync(["--label", "trial-1", "--out", file], Simulated, output, TestContext.Current.CancellationToken);

        var report = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        Assert.Contains("`trial-1`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Simulated", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Modell", report, StringComparison.Ordinal);
    }

    // A real provider needs a label and the model of the check, else nothing is asked and paid.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RealProvider_WithoutLabelOrCheck_StopsBeforeAnything(bool labelled, bool withModeration)
    {
        using var output = new StringWriter();
        string[] args = labelled ? ["--label", "trial-1", "--out", file] : ["--out", file];

        var exit = await Run.RunAsync(args, configuration => configuration.AddInMemoryCollection(
        [
            KeyValuePair.Create("Ai:Provider", (string?)"OpenAiCompatible"),
            KeyValuePair.Create("Ai:OpenAiCompatible:Endpoint", (string?)"https://127.0.0.1:9/v1"),
            KeyValuePair.Create("Ai:OpenAiCompatible:Model", (string?)"some-model"),
            KeyValuePair.Create("Ai:OpenAiCompatible:ApiKey", (string?)"not-a-key"),
            KeyValuePair.Create("Ai:Moderation:Model", withModeration ? "some-classifier" : null),
        ]), output, TestContext.Current.CancellationToken);

        Assert.Equal(Run.Stopped, exit);
        Assert.StartsWith("Stopped:", output.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(file));
    }

    [Theory]
    [InlineData("--runs", "0")]
    [InlineData("--max-cost", "-1")]
    [InlineData("--unknown", "x")]
    [InlineData("--runs")]
    [InlineData("--label", "Mein Modell/1")]
    [InlineData("--label")]
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