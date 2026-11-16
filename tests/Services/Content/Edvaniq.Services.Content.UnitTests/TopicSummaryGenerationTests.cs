using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #344: the prompt of a summary, its frame per length and the check of the answer. Since summary-2 the prompt names the
// frame worked out from the material and the check gives a leeway around it, a real model missed the old one in 9 of 12.
public sealed class TopicSummaryGenerationTests
{
    // The hash of the system prompt of each version over every profile and length, for material long enough for the full
    // frames. A changed sentence needs a new version, and its hash goes here.
    private static readonly Dictionary<string, string> Prompts = new()
    {
        ["summary-1"] = "b814e52d092a57d5fdb331472420287db0bb317fbef5d32711f90baf8eae0b17",
        ["summary-2"] = "90a056846c5be229738dd57d3d5b59b908d28eefd58bd417ef82df516d83a4ca",
    };

    [Fact]
    public void Prompt_MatchesItsVersion() =>
        Assert.Equal(
            Prompts[TopicSummaryGeneration.PromptVersion],
            PromptHash.Of(profile => string.Join(
                "\n\0\n", Enum.GetValues<SummaryLength>().Select(length => TopicSummaryGeneration.System(profile, length, 1_000)))));

    [Theory]
    [InlineData(SummaryLength.Short, "Länge: kurz", "mit 40 bis 90 Wörtern")]
    [InlineData(SummaryLength.Standard, "Länge: standard", "mit 150 bis 250 Wörtern")]
    [InlineData(SummaryLength.Detailed, "Länge: ausführlich", "mit 350 bis 600 Wörtern")]
    public void Prompt_NamesItsLength_AndTheWordsOfItsFrame(SummaryLength length, string name, string frame)
    {
        var system = TopicSummaryGeneration.System(LearnerProfile.Default, length, 1_000).ReplaceLineEndings("\n");

        Assert.StartsWith(TopicSummaryGeneration.Task + "\n" + name + "\n", system, StringComparison.Ordinal);
        Assert.Contains(frame, system, StringComparison.Ordinal);
        Assert.DoesNotContain("halb so viele", system, StringComparison.Ordinal);
    }

    // The prompt asks for exactly the frame the check holds the answer to, whatever the material.
    [Theory]
    [InlineData(SummaryLength.Short, 120)]
    [InlineData(SummaryLength.Detailed, 120)]
    [InlineData(SummaryLength.Standard, 450)]
    public void Prompt_AsksForTheFrameOfItsMaterial(SummaryLength length, int materialWords)
    {
        var (min, max) = TopicSummaryGeneration.Frame(length, materialWords);

        Assert.Contains($"mit {min} bis {max} Wörtern", TopicSummaryGeneration.System(LearnerProfile.Default, length, materialWords), StringComparison.Ordinal);
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

    // Models don't count words exactly: a fifth below and a quarter above the frame still hold up, further off not.
    [Theory]
    [InlineData(32, true)]
    [InlineData(31, false)]
    [InlineData(113, true)]
    [InlineData(114, false)]
    public void Answer_NearTheFrame_HoldsUp(int words, bool holds) =>
        Assert.Equal(holds, TopicSummaryGeneration.Read(Answer(Words(words), "[1]"), 3, SummaryLength.Short, 1_000) is not null);

    [Theory]
    [InlineData(20, "[1]", SummaryRefusal.TooShort)]
    [InlineData(200, "[1]", SummaryRefusal.TooLong)]
    [InlineData(60, "[]", SummaryRefusal.NoSection)]
    [InlineData(60, "[0]", SummaryRefusal.NoSection)]
    [InlineData(60, "[4]", SummaryRefusal.NoSection)]
    [InlineData(60, "[\"1\"]", SummaryRefusal.NoSection)]
    public void Answer_OutsideTheFrame_OrWithoutValidSources_IsRefused_WithItsReason(int words, string sections, SummaryRefusal refusal)
    {
        var check = TopicSummaryGeneration.Check(Answer(Words(words), sections), 3, SummaryLength.Short, 1_000);

        Assert.Null(check.Draft);
        Assert.Equal((refusal, words, (40, 90)), (check.Refusal, check.Words, check.Frame));
    }

    // A number that is no section of the material is left out, the others still count.
    [Fact]
    public void Answer_WithAWrongSectionAmongRightOnes_KeepsTheRightOnes() =>
        Assert.Equal([0, 2], TopicSummaryGeneration.Read(Answer(Words(60), "[1,9,0,3]"), 3, SummaryLength.Short, 1_000)!.Used);

    [Theory]
    [InlineData("kein JSON")]
    [InlineData("""{"summary":"Text"}""")]
    [InlineData("""{"summary":1,"sections":[1]}""")]
    [InlineData("[]")]
    public void Answer_OfAnotherForm_IsRefused(string answer) =>
        Assert.Equal(SummaryRefusal.NoJson, TopicSummaryGeneration.Check(answer, 3, SummaryLength.Short, 1_000).Refusal);

    // Material too short for a full frame gets a share of its words: a detailed text is never longer than the material,
    // and the lengths still differ.
    [Theory]
    [InlineData(1_000, 40, 90, 150, 250, 350, 600)]
    [InlineData(600, 40, 90, 150, 250, 350, 600)]
    [InlineData(300, 40, 90, 105, 165, 180, 300)]
    [InlineData(120, 18, 36, 42, 66, 72, 120)]
    [InlineData(30, 5, 9, 11, 17, 18, 30)]
    public void ShortMaterial_ScalesTheFrames(int materialWords, int shortMin, int shortMax, int standardMin, int standardMax, int detailedMin, int detailedMax)
    {
        Assert.Equal((shortMin, shortMax), TopicSummaryGeneration.Frame(SummaryLength.Short, materialWords));
        Assert.Equal((standardMin, standardMax), TopicSummaryGeneration.Frame(SummaryLength.Standard, materialWords));
        Assert.Equal((detailedMin, detailedMax), TopicSummaryGeneration.Frame(SummaryLength.Detailed, materialWords));
    }

    [Fact]
    public void Frames_DoNotOverlap()
    {
        foreach (var materialWords in new[] { 100, 300, 1_000 })
        {
            var (_, shortMax) = TopicSummaryGeneration.Frame(SummaryLength.Short, materialWords);
            var (standardMin, standardMax) = TopicSummaryGeneration.Frame(SummaryLength.Standard, materialWords);
            var (detailedMin, _) = TopicSummaryGeneration.Frame(SummaryLength.Detailed, materialWords);

            Assert.True(shortMax < standardMin && standardMax < detailedMin, $"{materialWords} words");
        }
    }

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Wort", count));

    private static string Answer(string text, string sections) => $$"""{"summary":"{{text}}","sections":{{sections}}}""";
}