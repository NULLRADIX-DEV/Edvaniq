using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #411: the demo answers hold up as the worker reads them, for every prompt the demo material brings: the topics of each
// document, at least a full pool of questions for each topic, mixed as the prompt asks, and a summary in every length.
// The prompts are built like the worker builds them, a section order of Knowledge doesn't matter.
public class DemoReplayTests
{
    private static readonly LearnerProfile Profile = LearnerProfile.Default;

    [Fact]
    public void Answers_AreForTheCurrentPromptVersions() =>
        Assert.Equal(
            new DemoVersions(TopicAnalysis.PromptVersion, QuestionGeneration.PromptVersion, TopicSummaryGeneration.PromptVersion),
            DemoReplay.Versions);

    [Fact]
    public void EveryDocument_GetsItsTopics_EachWithSections()
    {
        var names = new List<string>();
        foreach (var document in DemoMaterial.Documents)
        {
            var (ids, texts) = Sections(document);
            var answer = DemoReplay.Answer(TopicAnalysis.System(Profile), TopicAnalysis.User(texts));

            var topics = TopicAnalysis.Read(Assert.IsType<string>(answer), ids, texts);

            Assert.NotNull(topics);
            Assert.All(topics, topic => Assert.NotEmpty(topic.SectionIds));
            Assert.Equal(ids.Order(), topics.SelectMany(topic => topic.SectionIds).Order());
            names.AddRange(topics.Select(topic => topic.Name));
        }

        Assert.True(names.Count >= 6, $"{names.Count} topics");
        Assert.Equal(DemoReplay.Topics, names);
    }

    [Fact]
    public void EveryTopic_GetsAFullPool_MixedAsAsked()
    {
        foreach (var (name, texts) in TopicMaterial())
        {
            var ids = texts.Select(_ => Guid.NewGuid()).ToList();
            foreach (var order in new[] { texts, [.. texts.AsEnumerable().Reverse()] })
            {
                var answer = DemoReplay.Answer(QuestionGeneration.System(Profile), QuestionGeneration.User(order));

                var questions = QuestionGeneration.Read(Assert.IsType<string>(answer), ids);

                Assert.NotNull(questions);
                Assert.True(questions.Count >= QuestionPool.MinQuestions, $"{name}: {questions.Count} questions");
                Assert.All(Enum.GetValues<QuestionType>(), type => Assert.True(questions.Count(question => question.Type == type) >= 5, $"{name}: {type}"));
                Assert.Equal([1, 2, 3], questions.Select(question => question.Difficulty).Distinct().Order());
            }
        }
    }

    [Fact]
    public void EveryTopic_GetsASummaryInEveryLength()
    {
        foreach (var (name, texts) in TopicMaterial())
        {
            var words = texts.Sum(TopicSummaryGeneration.WordCount);
            foreach (var length in Enum.GetValues<SummaryLength>())
            {
                var answer = DemoReplay.Answer(TopicSummaryGeneration.System(Profile, length), TopicSummaryGeneration.User(texts));

                Assert.True(
                    answer is not null && TopicSummaryGeneration.Read(answer, texts.Count, length, words) is not null,
                    $"{name} {length}: {(answer is null ? "no answer" : $"{TopicSummaryGeneration.WordCount(answer)} words in the answer, frame {TopicSummaryGeneration.Frame(length, words)}")}");
            }
        }
    }

    // A top-up round, other material and an unknown task get nothing: the worker fails them, nothing is made up.
    [Fact]
    public void OtherPrompts_GetNoAnswer()
    {
        var (_, texts) = TopicMaterial().First();

        Assert.Null(DemoReplay.Answer(QuestionGeneration.System(Profile, 15), QuestionGeneration.User(texts)));
        Assert.Null(DemoReplay.Answer(QuestionGeneration.System(Profile), QuestionGeneration.User(["Ein ganz anderer Text."])));
        Assert.Null(DemoReplay.Answer("edvaniq:unknown", QuestionGeneration.User(texts)));
    }

    private static (List<Guid> Ids, List<string> Texts) Sections(DemoDocument document)
    {
        var spans = SectionSplitter.Split(document.Text);
        return ([.. spans.Select(_ => Guid.NewGuid())], [.. spans.Select(span => document.Text.Substring(span.Start, span.Length))]);
    }

    // The material of each topic, as the topics answer assigns the sections.
    private static IEnumerable<(string Name, List<string> Texts)> TopicMaterial()
    {
        foreach (var document in DemoMaterial.Documents)
        {
            var (ids, texts) = Sections(document);
            var topics = TopicAnalysis.Read(DemoReplay.Answer(TopicAnalysis.System(Profile), TopicAnalysis.User(texts))!, ids, texts)!;
            foreach (var topic in topics)
            {
                yield return (topic.Name, [.. topic.SectionIds.Select(id => texts[ids.IndexOf(id)])]);
            }
        }
    }
}