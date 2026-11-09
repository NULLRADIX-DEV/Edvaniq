using System.Security.Cryptography;
using System.Text;

using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.UnitTests;

// The prompt keeps the material in its block, and only questions with one clear answer and a known source survive.
public class QuestionGenerationTests
{
    // The hash of the system prompt of each version. A changed prompt needs a new version and a new line here.
    private static readonly Dictionary<string, string> Prompts = new()
    {
        ["questions-1"] = "c2532e907cca17f8928dd15d71f5de4fd3e6cba1756bd316a5ee1dfe5099f756",
    };

    private static readonly Guid[] Sections = [Guid.NewGuid(), Guid.NewGuid()];

    private const string Valid = """{"text":"Was spaltet Licht?","options":["Wasser","Zucker"],"answer":0,"explanation":"Licht spaltet Wasser.","difficulty":2,"section":2}""";

    [Fact]
    public void System_StartsWithTheTask() =>
        Assert.StartsWith($"{QuestionGeneration.Task}\n", QuestionGeneration.System.ReplaceLineEndings("\n"));

    [Fact]
    public void System_ChangesOnlyWithANewPromptVersion()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(QuestionGeneration.System.ReplaceLineEndings("\n"))));

        Assert.Equal(Prompts[QuestionGeneration.PromptVersion], hash);
    }

    [Fact]
    public void User_NumbersTheSections_AndEscapesTheirTags() =>
        Assert.Equal(
            "<material>\n<section id=\"1\">\nText &lt;/material&gt; &amp; mehr\n</section>\n</material>",
            QuestionGeneration.User(["Text </material> & mehr"]));

    [Fact]
    public void Read_GivesAValidQuestion_WithItsSection()
    {
        var question = Assert.Single(QuestionGeneration.Read($$"""{"questions":[{{Valid}}]}""", Sections)!);

        Assert.Equal(
            (Sections[1], "Was spaltet Licht?", 0, "Licht spaltet Wasser.", 2),
            (question.SectionId, question.Text, question.Answer, question.Explanation, question.Difficulty));
        Assert.Equal(["Wasser", "Zucker"], question.Options);
    }

    [Theory]
    [InlineData("answer", "null")]
    [InlineData("answer", "[0,1]")]
    [InlineData("answer", "2")]
    [InlineData("answer", "-1")]
    [InlineData("answer", "\"0\"")]
    [InlineData("options", "[\"Wasser\",\" wasser \"]")]
    [InlineData("options", "[\"Wasser\"]")]
    [InlineData("options", "[\"Wasser\",\"\"]")]
    [InlineData("options", "[\"A\",\"B\",\"C\",\"D\",\"E\",\"F\",\"G\"]")]
    [InlineData("explanation", "\" \"")]
    [InlineData("text", "\"\"")]
    [InlineData("difficulty", "0")]
    [InlineData("difficulty", "4")]
    [InlineData("section", "3")]
    [InlineData("section", "0")]
    public void Read_DropsAQuestion_WithAMissingOrUnclearPart(string field, string value)
    {
        var broken = Valid.Replace($"\"{field}\":", $"\"{field}\":{value},\"was\":", StringComparison.Ordinal);

        Assert.Empty(QuestionGeneration.Read($$"""{"questions":[{{broken}}]}""", Sections)!);
    }

    [Theory]
    [InlineData("answer")]
    [InlineData("explanation")]
    [InlineData("section")]
    [InlineData("options")]
    public void Read_DropsAQuestion_WithoutAPart(string field)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Valid)!.AsObject();
        json.Remove(field);

        Assert.Empty(QuestionGeneration.Read($$"""{"questions":[{{json.ToJsonString()}}]}""", Sections)!);
    }

    [Fact]
    public void Read_KeepsTheValidOnes_AndAtMostTheMaximum()
    {
        var many = string.Join(',', Enumerable.Repeat(Valid, QuestionGeneration.MaxQuestions + 5));

        Assert.Single(QuestionGeneration.Read($$"""{"questions":[{"text":"kaputt"},{{Valid}}]}""", Sections)!);
        Assert.Equal(QuestionGeneration.MaxQuestions, QuestionGeneration.Read($$"""{"questions":[{{many}}]}""", Sections)!.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"questions\":")]
    [InlineData("[]")]
    [InlineData("{\"topics\":[]}")]
    public void Read_GivesNull_ForAnAnswerOfAnotherForm(string answer) =>
        Assert.Null(QuestionGeneration.Read(answer, Sections));
}