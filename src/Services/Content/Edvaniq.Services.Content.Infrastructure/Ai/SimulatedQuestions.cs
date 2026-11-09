using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// 15 to 20 true/false questions from the sentences of the numbered sections of QuestionGeneration.User, one sentence
// each and round again when there are fewer. Heading lines count only when there is nothing else. How many and how hard
// comes from the prompt's randomness. The sentence is true and its own explanation, its section the source.
internal sealed partial class SimulatedQuestions : ISimulatedAnswer
{
    public bool Answers(SimulatedPrompt prompt) => ContentAi.IsTask(prompt, ContentAi.QuestionsTask);

    public string Answer(SimulatedPrompt prompt)
    {
        var sections = SectionPattern().Matches(prompt.User)
            .Select(match => (Number: int.Parse(match.Groups[1].Value), Lines: match.Groups[2].Value.Split('\n', StringSplitOptions.TrimEntries)))
            .ToList();
        var sentences = Sentences(sections, line => !line.StartsWith('#'));
        if (sentences.Count == 0)
        {
            sentences = Sentences(sections, _ => true);
        }

        if (sentences.Count == 0)
        {
            sentences = [(1, "Das Material ist leer")];
        }

        var random = prompt.Random();
        var questions = Enumerable.Range(0, random.Next(15, 21))
            .Select(index => sentences[index % sentences.Count])
            .Select(sentence => new
            {
                Text = $"Stimmt das: {sentence.Text}?",
                Options = new[] { "Richtig", "Falsch" },
                Answer = 0,
                Explanation = sentence.Text,
                Difficulty = random.Next(1, 4),
                Section = sentence.Section,
            })
            .ToList();

        return JsonSerializer.Serialize(new { questions }, JsonSerializerOptions.Web);
    }

    private static List<(int Section, string Text)> Sentences(List<(int Number, string[] Lines)> sections, Func<string, bool> counts) =>
        [.. sections.SelectMany(section => section.Lines
            .Where(counts)
            .Select(line => WebUtility.HtmlDecode(line.TrimStart('#', ' ')))
            .SelectMany(line => line.Split(['.', '!', '?'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(text => (section.Number, text[..Math.Min(text.Length, MaxSentence)])))];

    // Leaves room in a question's text for the words around the sentence.
    private const int MaxSentence = 400;

    [GeneratedRegex("<section id=\"(\\d+)\">\n(.*?)\n</section>", RegexOptions.Singleline)]
    private static partial Regex SectionPattern();
}