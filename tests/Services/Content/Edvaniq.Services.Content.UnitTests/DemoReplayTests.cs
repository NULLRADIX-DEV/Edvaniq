using System.Text.RegularExpressions;

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

    // #340: the demo pools bring their cards, all of them through the check.
    [Fact]
    public void EveryTopic_GetsCards_ThatAllHoldUp()
    {
        foreach (var (name, texts) in TopicMaterial())
        {
            var ids = texts.Select(_ => Guid.NewGuid()).ToList();
            var answer = Assert.IsType<string>(DemoReplay.Answer(QuestionGeneration.System(Profile), QuestionGeneration.User(texts)));

            var cards = QuestionGeneration.ReadCards(answer, ids);

            Assert.NotNull(cards);
            Assert.True(cards.Count >= 4, $"{name}: {cards.Count} cards");
            Assert.Equal(Regex.Matches(answer, "\"front\"").Count, cards.Count);
        }
    }

    // #342: the numbers of the reference run on the hand-written demo pools, a floor so they don't flag good questions.
    [Fact]
    public void DemoPools_HoldUp_ByTheQualityNumbers()
    {
        var tally = new QualityTally();
        foreach (var (_, texts) in TopicMaterial())
        {
            var ids = texts.Select(_ => Guid.NewGuid()).ToList();
            var answer = Assert.IsType<string>(DemoReplay.Answer(QuestionGeneration.System(Profile), QuestionGeneration.User(texts)));
            tally.Add(
                QuestionGeneration.Read(answer, ids),
                QuestionGeneration.ReadCards(answer, ids) ?? [],
                ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => texts[pair.index]));
        }

        // A bit below what the demo pools have. The longest option as answer gives it away, the demo keeps that rare.
        Assert.Equal(0, tally.Unusable);
        Assert.True(tally.LongestRight.Rate <= 0.15, $"longest {tally.LongestRight.Rate}");
        Assert.True(tally.Sourced.Rate >= 0.8, $"sourced {tally.Sourced.Rate}");
        Assert.True(tally.Distinct.Rate >= 0.9, $"distinct {tally.Distinct.Rate}");
        Assert.True(tally.Explained.Rate >= 0.8, $"explained {tally.Explained.Rate}");
        Assert.Equal(1, tally.WellFormed.Rate);
        Assert.True(tally.CardsSourced.Rate >= 0.9, $"cards {tally.CardsSourced.Rate}");
    }

    [Fact]
    public void EveryTopic_GetsASummaryInEveryLength()
    {
        var failures = new List<string>();
        foreach (var (name, texts) in TopicMaterial())
        {
            var words = texts.Sum(TopicSummaryGeneration.WordCount);
            foreach (var length in Enum.GetValues<SummaryLength>())
            {
                var answer = DemoReplay.Answer(TopicSummaryGeneration.System(Profile, length, words), TopicSummaryGeneration.User(texts));
                var check = answer is null ? null : TopicSummaryGeneration.Check(answer, texts.Count, length, words);
                if (check?.Draft is null)
                {
                    failures.Add($"{name} {length}: {(check is null ? "no answer" : $"{check.Refusal}, {check.Words} words, frame {check.Frame}, material {words}")}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // A top-up round, other material and an unknown task get nothing: the worker fails them, nothing is made up.
    [Fact]
    public void OtherPrompts_GetNoAnswer()
    {
        var (_, texts) = TopicMaterial().First();

        Assert.Null(DemoReplay.Answer(QuestionGeneration.System(Profile, 15, QuestionGeneration.MaxQuestions), QuestionGeneration.User(texts)));
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