using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// Answers the worker's AI calls for the demo material (#411) from Demo/answers.json, written for it by hand: the topics
// of each document, the questions of each topic and its summaries. A prompt is known by its task and its sections, not
// by its exact text, so the order in which Knowledge names a topic's sections doesn't matter. A prompt the answers don't
// know, a top-up round or an answer for another prompt version gets null: the caller fails it, and the demo tests say
// which answers to write.
public static partial class DemoReplay
{
    private static readonly Lazy<Replay> Answers = new(Load);

    // The prompt versions the answers were written for. Another version means new prompts, the answers need a look.
    public static DemoVersions Versions => Answers.Value.File.Versions;

    // The topic names, in the order of the documents.
    public static IReadOnlyList<string> Topics => [.. Answers.Value.File.Topics.Select(topic => topic.Title)];

    public static string? Answer(string system, string user)
    {
        var replay = Answers.Value;
        var sections = PromptMaterial.Sections(user);
        if (system.StartsWith(TopicAnalysis.Task, StringComparison.Ordinal))
        {
            return replay.File.Versions.Topics == TopicAnalysis.PromptVersion ? replay.TopicsOf(sections) : null;
        }

        if (system.StartsWith(QuestionGeneration.Task, StringComparison.Ordinal))
        {
            return replay.File.Versions.Questions == QuestionGeneration.PromptVersion && system.Contains(FirstRound, StringComparison.Ordinal)
                ? replay.QuestionsOf(sections)
                : null;
        }

        if (system.StartsWith(TopicSummaryGeneration.Task, StringComparison.Ordinal)
            && replay.File.Versions.Summary == TopicSummaryGeneration.PromptVersion
            && LengthOf(system) is { } length)
        {
            return replay.SummaryOf(sections, length);
        }

        return null;
    }

    // The first round asks for all questions, a top-up names how many there are already.
    private static readonly string FirstRound = $"Schreib {QuestionGeneration.MaxQuestions} Fragen,";

    private static SummaryLength? LengthOf(string system) =>
        WordFrame().Match(system) is { Success: true } match
            ? Enum.GetValues<SummaryLength>()
                .Cast<SummaryLength?>()
                .FirstOrDefault(length => TopicSummaryGeneration.WordsOf(length!.Value) == (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)))
            : null;

    [GeneratedRegex(@"Zusammenfassung mit (\d+) bis (\d+) Wörtern")]
    private static partial Regex WordFrame();

    private static Replay Load()
    {
        var assembly = typeof(DemoReplay).Assembly;
        using var stream = assembly.GetManifestResourceStream("DemoAnswers.answers.json")
            ?? throw new InvalidOperationException("The demo answers are missing from the assembly.");
        var file = JsonSerializer.Deserialize<DemoAnswersFile>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The demo answers are empty.");
        return new Replay(file);
    }

    private sealed class Replay(DemoAnswersFile file)
    {
        private readonly Dictionary<string, List<(string Text, string Heading)>> sectionsOf = DemoMaterial.Documents.ToDictionary(
            document => document.Title,
            document => SectionSplitter.Split(document.Text)
                .Select(span => (document.Text.Substring(span.Start, span.Length), SectionSplitter.HeadingOf(document.Text, span.Start) ?? ""))
                .ToList());

        public DemoAnswersFile File { get; } = file;

        // The topics of a whole document, its sections in their order.
        public string? TopicsOf(IReadOnlyList<string> prompt)
        {
            if (sectionsOf.FirstOrDefault(entry => entry.Value.Select(section => section.Text).SequenceEqual(prompt)) is not { Key: { } title, Value: var sections })
            {
                return null;
            }

            int SectionOf(string heading) => sections.FindIndex(section => section.Heading == heading) + 1;
            return JsonSerializer.Serialize(
                new
                {
                    topics = File.Topics.Where(topic => topic.Document == title).Select(topic => new
                    {
                        title = topic.Title,
                        sections = sections.Select((section, index) => (section.Heading, Number: index + 1))
                            .Where(section => topic.Headings.Contains(section.Heading))
                            .Select(section => section.Number),
                        terms = topic.Terms.Select(term => new { term = term.Term, section = SectionOf(term.Heading), definition = term.Definition }),
                        subtopics = Array.Empty<object>(),
                    }),
                },
                JsonSerializerOptions.Web);
        }

        public string? QuestionsOf(IReadOnlyList<string> prompt)
        {
            if (TopicOf(prompt) is not { } topic || !File.Questions.TryGetValue(topic.Title, out var questions))
            {
                return null;
            }

            // The questions come from the section under the topic's own heading.
            var own = SectionsOf(topic).FirstOrDefault(item => item.Heading == topic.Title).Text;
            var section = own is null ? 1 : Math.Max(1, prompt.ToList().IndexOf(own) + 1);
            return JsonSerializer.Serialize(
                new
                {
                    questions = questions.Select(question => new
                    {
                        type = question.Type,
                        text = question.Text,
                        options = question.Options,
                        answers = question.Answers,
                        explanation = question.Explanation,
                        difficulty = question.Difficulty,
                        section,
                    }),
                },
                JsonSerializerOptions.Web);
        }

        public string? SummaryOf(IReadOnlyList<string> prompt, SummaryLength length)
        {
            if (TopicOf(prompt) is not { } topic || !File.Summaries.TryGetValue(topic.Title, out var summaries))
            {
                return null;
            }

            var text = length switch
            {
                SummaryLength.Short => summaries.Short,
                SummaryLength.Standard => summaries.Standard,
                _ => summaries.Detailed,
            };
            return JsonSerializer.Serialize(new { summary = text, sections = Enumerable.Range(1, prompt.Count) }, JsonSerializerOptions.Web);
        }

        // The topic whose sections are the prompt's, in any order.
        private DemoTopic? TopicOf(IReadOnlyList<string> prompt) =>
            File.Topics.FirstOrDefault(topic => SectionsOf(topic).Select(section => section.Text).Order(StringComparer.Ordinal)
                .SequenceEqual(prompt.Order(StringComparer.Ordinal)));

        private List<(string Text, string Heading)> SectionsOf(DemoTopic topic) =>
            sectionsOf.TryGetValue(topic.Document, out var sections) ? [.. sections.Where(section => topic.Headings.Contains(section.Heading))] : [];
    }
}

public sealed record DemoAnswersFile(
    DemoVersions Versions,
    IReadOnlyList<DemoTopic> Topics,
    IReadOnlyDictionary<string, IReadOnlyList<DemoQuestion>> Questions,
    IReadOnlyDictionary<string, DemoSummaries> Summaries);

public sealed record DemoVersions(string Topics, string Questions, string Summary);

// A topic of a document, given by the headings of its sections.
public sealed record DemoTopic(string Document, string Title, IReadOnlyList<string> Headings, IReadOnlyList<DemoTerm> Terms);

public sealed record DemoTerm(string Term, string Heading, string Definition);

public sealed record DemoQuestion(string Type, string Text, IReadOnlyList<string> Options, IReadOnlyList<int> Answers, string Explanation, int Difficulty);

public sealed record DemoSummaries(string Short, string Standard, string Detailed);