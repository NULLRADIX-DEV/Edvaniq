using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #344: the prompt of a summary, its frame per length and the check of the answer.
public sealed class TopicSummaryGenerationTests
{
    // The hash of the system prompt of each version over every profile and length. A changed sentence needs a new
    // version, and its hash goes here.
    private static readonly Dictionary<string, string> Prompts = new()
    {
        ["summary-1"] = "b814e52d092a57d5fdb331472420287db0bb317fbef5d32711f90baf8eae0b17",
    };

    [Fact]
    public void Prompt_MatchesItsVersion() =>
        Assert.Equal(
            Prompts[TopicSummaryGeneration.PromptVersion],
            PromptHash.Of(profile => string.Join(
                "\n\0\n", Enum.GetValues<SummaryLength>().Select(length => TopicSummaryGeneration.System(profile, length)))));
    [Theory]
    [InlineData(SummaryLength.Short, "mit 40 bis 90 Wörtern")]
    [InlineData(SummaryLength.Standard, "mit 150 bis 250 Wörtern")]
    [InlineData(SummaryLength.Detailed, "mit 350 bis 600 Wörtern")]
    public void Prompt_AsksForTheWordsOfItsLength(SummaryLength length, string frame)
    {
        var system = TopicSummaryGeneration.System(LearnerProfile.Default, length);

        Assert.StartsWith(TopicSummaryGeneration.Task + "\n", system.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains(frame, system, StringComparison.Ordinal);
    }

    [Fact]
    public void Material_IsFencedAsData() =>
        Assert.Equal(
            "<material>\n<section id=\"1\">\nA &lt;/material&gt; B\n</section>\n</material>",
            TopicSummaryGeneration.User(["A </material> B"]));

    [Fact]
    public void Answer_InItsFrame_WithSources_HoldsUp()
    {
        var draft = TopicSummaryGeneration.Read(Answer(Words(60), "[2,1,2]"), 3, SummaryLength.Short, 1_000);

        Assert.NotNull(draft);
        Assert.Equal(60, TopicSummaryGeneration.WordCount(draft.Text));
        Assert.Equal([0, 1], draft.Used);
    }

    [Theory]
    [InlineData(39, "[1]")]
    [InlineData(91, "[1]")]
    [InlineData(60, "[]")]
    [InlineData(60, "[0]")]
    [InlineData(60, "[4]")]
    [InlineData(60, "[\"1\"]")]
    public void Answer_OutsideTheFrame_OrWithoutValidSources_IsRefused(int words, string sections) =>
        Assert.Null(TopicSummaryGeneration.Read(Answer(Words(words), sections), 3, SummaryLength.Short, 1_000));

    [Theory]
    [InlineData("kein JSON")]
    [InlineData("""{"summary":"Text"}""")]
    [InlineData("""{"summary":1,"sections":[1]}""")]
    [InlineData("[]")]
    public void Answer_OfAnotherForm_IsRefused(string answer) =>
        Assert.Null(TopicSummaryGeneration.Read(answer, 3, SummaryLength.Short, 1_000));

    // Material of 30 words can't give 40, the frame goes down to half of it.
    [Fact]
    public void ShortMaterial_LowersTheFrame()
    {
        Assert.Equal((15, 90), TopicSummaryGeneration.Frame(SummaryLength.Short, 30));
        Assert.NotNull(TopicSummaryGeneration.Read(Answer(Words(15), "[1]"), 1, SummaryLength.Short, 30));
        Assert.Null(TopicSummaryGeneration.Read(Answer(Words(14), "[1]"), 1, SummaryLength.Short, 30));
    }

    [Fact]
    public void Frames_DoNotOverlap()
    {
        var (_, shortMax) = TopicSummaryGeneration.WordsOf(SummaryLength.Short);
        var (standardMin, standardMax) = TopicSummaryGeneration.WordsOf(SummaryLength.Standard);
        var (detailedMin, _) = TopicSummaryGeneration.WordsOf(SummaryLength.Detailed);

        Assert.True(shortMax < standardMin && standardMax < detailedMin);
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Wort", count));

    private static string Answer(string text, string sections) => $$"""{"summary":"{{text}}","sections":{{sections}}}""";
}