using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// The classifier of the moderation, simulated: safe, unless the text carries a marker like "[moderation:S12]", then
// unsafe with the codes of all markers. Tests and test accounts flag material and output with it on purpose.
public sealed partial class SimulatedModeration : ISimulatedAnswer
{
    public static string Marker(string code) => $"[moderation:{code}]";

    public bool Answers(SimulatedPrompt prompt) => prompt.Task == ModerationCheck.Task;

    public string Answer(SimulatedPrompt prompt)
    {
        var codes = MarkerPattern().Matches(prompt.User).Select(match => match.Groups[1].Value).Distinct().ToList();
        return codes.Count == 0 ? "safe" : $"unsafe\n{string.Join(',', codes)}";
    }

    [GeneratedRegex(@"\[moderation:(S\d{1,2})\]")]
    private static partial Regex MarkerPattern();
}