using System.Text.Json;

using AngleSharp.Dom;

using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.TestData;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// Text from the user or the AI shows as text: markup in a title, a topic, a question, an option or an explanation stays
// the characters it is, and the page has exactly the elements it has with harmless text. Web and app render these
// same components from Client.UI.
public sealed class PlainTextTests
{
    private const string Harmless = "Photosynthese";

    private static readonly Guid DocumentId = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Subtopic = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    private static readonly Guid Attempt = Guid.Parse("8c9d0e1f-2a3b-4c4d-9e5f-6a7b8c9d0e1f");

    public static TheoryData<string> Payloads => [.. HostileText.Payloads];

    // AC3: the paragraphs of the reference injection case that speak to the AI.
    public static TheoryData<string> InjectionParagraphs => [.. ReadInjectionParagraphs()];

    // AC1: document title, failure reason, topic and subtopic.
    [Theory]
    [MemberData(nameof(Payloads))]
    public void TopicsPage_ShowsMarkupAsText(string text) => AssertTopicsPageShowsText(text);

    // AC1: topic name in the heading, question, option, right answer and explanation.
    [Theory]
    [MemberData(nameof(Payloads))]
    public void QuizPage_ShowsMarkupAsText(string text) => AssertQuizPageShowsText(text);

    // AC3: instructions aimed at the AI look like any other text, no tag of theirs becomes an element and nothing new
    // to click turns up.
    [Theory]
    [MemberData(nameof(InjectionParagraphs))]
    public void InjectionCase_LooksLikeAnyOtherText(string text)
    {
        AssertTopicsPageShowsText(text);
        AssertQuizPageShowsText(text);
    }

    [Fact]
    public void InjectionCase_HasParagraphsToShow() => Assert.True(ReadInjectionParagraphs().Count >= 3);

    private static void AssertTopicsPageShowsText(string text)
    {
        using var plain = new BunitContext();
        using var hostile = new BunitContext();
        var expected = RenderTopics(plain, Harmless);
        var page = RenderTopics(hostile, text);

        Assert.Equal(
            [$"Die Analyse von „{text}“ hat nicht geklappt.", text],
            page.FindAll(".material-status p").Select(paragraph => paragraph.TextContent));
        Assert.Equal([text, text], page.FindAll(".topic-list li").Select(OwnText));
        Assert.Equal([$"Üben zu „{text}“", $"Üben zu „{text}“"], page.FindAll(".practice-link").Select(link => link.TextContent));
        AssertSameElements(expected.Nodes, page.Nodes);
    }

    private static void AssertQuizPageShowsText(string text)
    {
        using var plain = new BunitContext();
        using var hostile = new BunitContext();
        var expected = RenderQuiz(plain, Harmless);
        var page = RenderQuiz(hostile, text);

        Assert.Equal($"Frage zu „{text}“", page.Find("h1").TextContent);
        Assert.Equal(text, page.Find("legend").TextContent);
        Assert.Equal([text, "Zweite Antwort"], page.FindAll(".quiz-option-text").Select(option => option.TextContent));
        var feedback = page.Find(".quiz-feedback").TextContent;
        Assert.Contains($"Richtige Antwort: {text}", feedback);
        Assert.Equal(text, page.Find(".quiz-explanation .markdown").TextContent);
        AssertSameElements(expected.Nodes, page.Nodes);
    }

    // Same elements in the same order, none of what a payload could become, no event handler from the text.
    private static void AssertSameElements(INodeList expected, INodeList actual)
    {
        Assert.Equal(Elements(expected).Select(Describe), Elements(actual).Select(Describe));
        Assert.DoesNotContain(Elements(actual), element => element.Matches(HostileText.Injected));
        Assert.DoesNotContain(Elements(actual).SelectMany(element => element.Attributes),
            attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<IElement> Elements(INodeList nodes) =>
        nodes.OfType<IElement>().SelectMany(element => element.DescendantsAndSelf<IElement>());

    private static string Describe(IElement element) =>
        element.TagName + (element.GetAttribute("href") is { } href ? $" href={href}" : "");

    private static IRenderedComponent<Topics> RenderTopics(BunitContext context, string text)
    {
        var material = new FakeLearningMaterial()
            .Documents(new DocumentInfo(DocumentId, text, DocumentStatus.Failed, DateTimeOffset.UnixEpoch, text))
            .Topics(new TopicInfo(Topic, text, [new SubtopicInfo(Subtopic, text, DocumentId)], DocumentId));
        context.Services.AddSingleton<ILearningMaterial>(material);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return context.Render<Topics>();
    }

    // Answered, so the feedback with solution and explanation is there too.
    private static IRenderedComponent<Practice> RenderQuiz(BunitContext context, string text)
    {
        var quiz = new FakeQuiz()
            .Ask(new AskedQuestion(Attempt, Topic, Guid.NewGuid(), text, [text, "Zweite Antwort"], 1))
            .Answer(new GradedAnswer(Attempt, 1, false, 0, text, 1000));
        context.Services.AddSingleton<IQuiz>(quiz);
        context.Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(new TopicInfo(Topic, text, [])));
        context.Services.AddQuietAnswerSync();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var page = context.Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));
        page.Find("#option-1").Change("1");
        page.Find("form").Submit();
        return page;
    }

    // The item's own name, without its link and its subtopics.
    private static string OwnText(IElement item) =>
        string.Concat(item.ChildNodes.OfType<IText>().Select(node => node.Data)).Trim();

    private static List<string> ReadInjectionParagraphs()
    {
        var folder = Path.Combine(Repository.Root, "tests", "ReferenceSet");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "reference-set.json")));
        var injection = manifest.RootElement.GetProperty("documents").EnumerateArray()
            .Single(document => document.GetProperty("kind").GetString() == "injection");
        var forbidden = injection.GetProperty("forbidden").EnumerateArray().Select(text => text.GetString()!).ToList();
        var file = File.ReadAllText(Path.Combine(folder, injection.GetProperty("file").GetString()!)).ReplaceLineEndings("\n");

        return file.Split("\n\n")
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => forbidden.Any(text => paragraph.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}