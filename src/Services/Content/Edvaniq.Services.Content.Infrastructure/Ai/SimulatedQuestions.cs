using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// 15 to 20 true/false questions from the sentences of the material, one sentence each and round again when there are
// fewer. How many and how hard comes from the prompt's randomness. The sentence is true and its own explanation.
internal sealed class SimulatedQuestions : ISimulatedAnswer
{
    public bool Answers(SimulatedPrompt prompt) => ContentAi.IsTask(prompt, ContentAi.QuestionsTask);

    public string Answer(SimulatedPrompt prompt)
    {
        string[] sentences = prompt.User.Split(['.', '!', '?', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            is { Length: > 0 } found ? found : ["Das Material ist leer"];
        var random = prompt.Random();
        var questions = Enumerable.Range(0, random.Next(15, 21))
            .Select(index => sentences[index % sentences.Length])
            .Select(sentence => new
            {
                Text = $"Stimmt das: {sentence}?",
                Options = new[] { "Richtig", "Falsch" },
                Answer = 0,
                Explanation = sentence,
                Difficulty = random.Next(1, 4),
            })
            .ToList();

        return JsonSerializer.Serialize(new { questions }, JsonSerializerOptions.Web);
    }
}
