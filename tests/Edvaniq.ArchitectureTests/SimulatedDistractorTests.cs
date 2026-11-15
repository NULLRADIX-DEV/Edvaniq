using System.Text.RegularExpressions;

using Edvaniq.Services.Content.Infrastructure.Ai;

namespace Edvaniq.ArchitectureTests;

// The walkthrough after a deploy answers a simulated question without knowing its solution: every option is right except
// the simulated AI's distractors. The options come shuffled since #1046, so the script must know the same distractors
// as the simulator, or the walkthrough fails on the server and the deploy rolls back.
public partial class SimulatedDistractorTests
{
    [Fact]
    public void Walkthrough_KnowsTheDistractorsOfTheSimulatedAi()
    {
        var script = File.ReadAllText(Path.Combine(Repository.Root, "deploy", "smoke-test.sh"));

        var list = Assert.Single(Distractors().Matches(script)).Groups[1].Value;

        Assert.Equal(ContentAi.SimulatedDistractors, Quoted().Matches(list).Select(match => match.Groups[1].Value));
    }

    [GeneratedRegex(@"^\s*distractors = \[(.*)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex Distractors();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex Quoted();
}