using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #294: the topic directory. Two levels that open one by one, each with its key terms and where it stands, the AI hint
// above, hidden topics apart at the end, and an empty state that tells analysis and no material apart.
public sealed class TopicDirectoryTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly Guid Pdf = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Pasted = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02");
    private static readonly Guid Gone = Guid.Parse("9e8d7c6b-5a49-4382-a716-151413121110");

    private static readonly Guid Photosynthese = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Lichtreaktion = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    private static readonly Guid Dunkelreaktion = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");
    private static readonly Guid Zellatmung = Guid.Parse("8c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f");

    private static readonly Guid[] Sections = [.. Enumerable.Range(1, 6).Select(i => Guid.Parse($"00000000-0000-4000-8000-00000000000{i}"))];

    private static readonly DocumentInfo PdfDocument = new(Pdf, "Biologie.pdf", DocumentStatus.Ready, DateTimeOffset.UnixEpoch, Source: DocumentSource.File);
    private static readonly DocumentInfo PastedDocument = new(Pasted, "Mitschrift", DocumentStatus.Ready, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Topics_ShowOnTwoLevels_ClosedAtFirst_WithoutAskingForDetails()
    {
        var directory = Directory();
        var page = RenderPage(directory);

        var topic = Toggle(page, "Photosynthese");
        Assert.Equal("false", topic.GetAttribute("aria-expanded"));
        Assert.Equal("H2", topic.ParentElement!.TagName);
        Assert.True(page.Find($"#{topic.GetAttribute("aria-controls")}").HasAttribute("hidden"));
        Assert.Empty(directory.TermCalls);
        Assert.Empty(directory.DocumentSectionCalls);

        topic.Click();

        Assert.Equal("true", Toggle(page, "Photosynthese").GetAttribute("aria-expanded"));
        var subtopic = Toggle(page, "Lichtreaktion");
        Assert.Equal("H3", subtopic.ParentElement!.TagName);
        Assert.Equal([Photosynthese], directory.TermCalls);
    }

    [Fact]
    public void OpenTopic_ShowsTermsAndSources_ByDocument_InOrder()
    {
        var directory = Directory();
        directory.WithTerms(Photosynthese, CallOutcome<IReadOnlyList<KeyTermInfo>>.Ok(
            [new KeyTermInfo("Chlorophyll", "Grüner Farbstoff, der Licht aufnimmt.", Pdf, Sections[0])]));
        directory.TopicSections[Photosynthese] =
        [
            new(Pasted, Sections[4]),
            new(Pdf, Sections[2]),
            new(Pdf, Sections[0]),
            new(Gone, Sections[5]),
        ];
        directory.DocumentSections[Pdf] = CallOutcome<IReadOnlyList<DocumentSectionInfo>>.Ok(
        [
            new(Sections[0], 0, 3, 4),
            new(Sections[1], 1, 5, 5),
            new(Sections[2], 2, 7, 7),
        ]);
        directory.DocumentSections[Pasted] = CallOutcome<IReadOnlyList<DocumentSectionInfo>>.Ok([new(Sections[4], 1)]);
        var page = RenderPage(directory);

        Toggle(page, "Photosynthese").Click();

        var panel = Panel(page, "Photosynthese");
        Assert.Equal("Chlorophyll", panel.QuerySelector("dl.topic-terms dt")!.TextContent);
        Assert.Equal("Grüner Farbstoff, der Licht aufnimmt.", panel.QuerySelector("dl.topic-terms dd")!.TextContent);
        Assert.Equal(
            ["Biologie.pdf: S. 3–4, S. 7", "Mitschrift: Abschnitt 2"],
            panel.QuerySelectorAll(":scope > ul.topic-sources > li").Select(item => item.TextContent));

        // The document that is gone is left out without asking for it.
        Assert.DoesNotContain(Gone, directory.DocumentSectionCalls);
    }

    [Fact]
    public void Details_LoadOnTheFirstOpening_AndEachDocumentOnce()
    {
        var directory = Directory();
        directory.TopicSections[Photosynthese] = [new(Pdf, Sections[0])];
        directory.TopicSections[Lichtreaktion] = [new(Pdf, Sections[1])];
        directory.DocumentSections[Pdf] = CallOutcome<IReadOnlyList<DocumentSectionInfo>>.Ok([new(Sections[0], 0, 1, 1), new(Sections[1], 1, 2, 2)]);
        var page = RenderPage(directory);

        Toggle(page, "Photosynthese").Click();
        Toggle(page, "Photosynthese").Click();
        Toggle(page, "Photosynthese").Click();
        Toggle(page, "Lichtreaktion").Click();

        Assert.Equal([Photosynthese, Lichtreaktion], directory.TermCalls);
        Assert.Equal([Pdf], directory.DocumentSectionCalls);
        Assert.Contains("Biologie.pdf: S. 2", Panel(page, "Lichtreaktion").TextContent);
    }

    [Fact]
    public void WithoutTermsOrSources_SaysSo()
    {
        var page = RenderPage(Directory());

        Toggle(page, "Zellatmung").Click();

        var panel = Panel(page, "Zellatmung").TextContent;
        Assert.Contains(FeedbackTexts.TopicDirectory.NoTerms, panel);
        Assert.Contains(FeedbackTexts.TopicDirectory.NoSources, panel);
    }

    [Fact]
    public void DetailsFailed_SaysSoForThatTopic_AndTriesAgain()
    {
        var directory = Directory().WithTerms(
            Photosynthese,
            CallOutcome<IReadOnlyList<KeyTermInfo>>.Failed(TraceId),
            CallOutcome<IReadOnlyList<KeyTermInfo>>.Ok([new KeyTermInfo("Chlorophyll", "Farbstoff.")]));
        var page = RenderPage(directory);

        Toggle(page, "Photosynthese").Click();

        var panel = Panel(page, "Photosynthese");
        Assert.Contains(FeedbackTexts.TopicDirectory.DetailsFailed, panel.QuerySelector("[role=status]")!.TextContent);
        Assert.Contains(TraceId, panel.TextContent);

        panel.QuerySelector(".state-actions button")!.Click();

        Assert.Equal("Chlorophyll", Panel(page, "Photosynthese").QuerySelector("dt")!.TextContent);
        Assert.Equal(2, directory.TermCalls.Count);
    }

    [Fact]
    public void DocumentSectionsFailed_FailTheDetails()
    {
        var directory = Directory();
        directory.TopicSections[Photosynthese] = [new(Pdf, Sections[0])];
        directory.DocumentSections[Pdf] = CallOutcome<IReadOnlyList<DocumentSectionInfo>>.Failed(TraceId);
        var page = RenderPage(directory);

        Toggle(page, "Photosynthese").Click();

        Assert.Contains(FeedbackTexts.TopicDirectory.DetailsFailed, Panel(page, "Photosynthese").TextContent);
    }

    // A document deleted between the list and the opening: Content says 404, the topic just doesn't name it.
    [Fact]
    public void DocumentGoneMeanwhile_IsLeftOut()
    {
        var directory = Directory();
        directory.TopicSections[Photosynthese] = [new(Pdf, Sections[0])];
        directory.DocumentSections[Pdf] = new CallOutcome<IReadOnlyList<DocumentSectionInfo>>(CallResult.NotFound);
        var page = RenderPage(directory);

        Toggle(page, "Photosynthese").Click();

        var panel = Panel(page, "Photosynthese").TextContent;
        Assert.Contains(FeedbackTexts.TopicDirectory.NoSources, panel);
        Assert.DoesNotContain(FeedbackTexts.TopicDirectory.DetailsFailed, panel);
    }

    [Fact]
    public void AiHint_StandsAboveTheTopics()
    {
        var page = RenderPage(Directory());

        var hint = page.Find(".directory-ai-hint");
        Assert.Equal(FeedbackTexts.TopicDirectory.AiHintTitle, hint.QuerySelector(".notice-title")!.TextContent);
        Assert.Contains(FeedbackTexts.TopicDirectory.AiHint, hint.TextContent);
    }

    [Fact]
    public void WithoutMaterial_SaysSo_AndLeadsToAddingSome()
    {
        var page = RenderPage(new FakeTopicDirectory());

        Assert.Contains(FeedbackTexts.TopicDirectory.NoMaterial, page.Find("[role=status]").TextContent);
        var next = page.Find(".state-actions a");
        Assert.Equal(("themen", FeedbackTexts.TopicDirectory.AddMaterial), (next.GetAttribute("href"), next.TextContent));
        Assert.Empty(page.FindAll(".directory-ai-hint"));
    }

    [Theory]
    [InlineData(DocumentStatus.Analyzing)]
    [InlineData(DocumentStatus.Uploaded)]
    public void WhileTheAnalysisRuns_SaysSo_AndLeadsToTheMaterial(DocumentStatus status)
    {
        var page = RenderPage(new FakeTopicDirectory
        {
            Documents = CallOutcome<IReadOnlyList<DocumentInfo>>.Ok([PdfDocument with { Status = status }]),
        });

        Assert.Contains(FeedbackTexts.TopicDirectory.Analyzing, page.Find("[role=status]").TextContent);
        Assert.Equal(FeedbackTexts.TopicDirectory.ToMaterial, page.Find(".state-actions a").TextContent);
    }

    [Fact]
    public void ListFailed_SaysSo_AndTriesAgain()
    {
        var directory = new FakeTopicDirectory { Topics = CallOutcome<IReadOnlyList<TopicInfo>>.Failed(TraceId) };
        var page = RenderPage(directory);

        Assert.Contains(FeedbackTexts.TopicDirectory.LoadFailed, page.Find("[role=status]").TextContent);

        directory.Topics = Directory().Topics;
        page.Find(".state-actions button").Click();

        Assert.NotNull(Toggle(page, "Photosynthese"));
        Assert.Equal(2, directory.TopicCalls);
    }

    // Hidden topics and hidden subtopics of visible ones sit apart, closed, at the end. A hidden subtopic names its topic.
    [Fact]
    public void HiddenTopics_AreApart_AndOpenToo()
    {
        var directory = Directory();
        directory.Topics = CallOutcome<IReadOnlyList<TopicInfo>>.Ok(
        [
            new(Photosynthese, "Photosynthese", [new(Lichtreaktion, "Lichtreaktion"), new(Dunkelreaktion, "Dunkelreaktion", Hidden: true)]),
            new(Zellatmung, "Zellatmung", [], Hidden: true),
        ]);
        var page = RenderPage(directory);

        Assert.Equal(["Photosynthese"], page.FindAll(".directory-list > li > .topic-entry > h2").Select(h => h.TextContent.Trim()));
        var hiddenToggle = page.Find(".directory-hidden h2 button");
        Assert.Equal(string.Format(FeedbackTexts.TopicDirectory.Hidden, 2), hiddenToggle.TextContent.Trim());
        Assert.Equal("false", hiddenToggle.GetAttribute("aria-expanded"));
        Assert.True(page.Find("#hidden-topics").HasAttribute("hidden"));

        hiddenToggle.Click();

        Assert.False(page.Find("#hidden-topics").HasAttribute("hidden"));
        Assert.Equal(["Zellatmung", "Dunkelreaktion"], page.FindAll("#hidden-topics .topic-entry > h3").Select(h => h.TextContent.Trim()));
        Assert.Contains(string.Format(FeedbackTexts.TopicDirectory.SubtopicOf, "Photosynthese"), page.Find("#hidden-topics").TextContent);

        Toggle(page, "Dunkelreaktion").Click();
        Assert.Equal([Dunkelreaktion], directory.TermCalls);
    }

    [Fact]
    public void WithoutHiddenTopics_ThereIsNoHiddenPart()
    {
        var page = RenderPage(Directory());

        Assert.Empty(page.FindAll(".directory-hidden"));
    }

    private static FakeTopicDirectory Directory() => new()
    {
        Topics = CallOutcome<IReadOnlyList<TopicInfo>>.Ok(
        [
            new(Photosynthese, "Photosynthese", [new(Lichtreaktion, "Lichtreaktion")], Pdf),
            new(Zellatmung, "Zellatmung", []),
        ]),
        Documents = CallOutcome<IReadOnlyList<DocumentInfo>>.Ok([PdfDocument, PastedDocument]),
    };

    private IRenderedComponent<TopicDirectory> RenderPage(FakeTopicDirectory directory)
    {
        Services.AddSingleton<ITopicDirectory>(directory);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<TopicDirectory>();
    }

    private static AngleSharp.Dom.IElement Toggle(IRenderedComponent<TopicDirectory> page, string name) =>
        page.FindAll("button.disclosure").Single(button => button.TextContent.Trim() == name);

    private static AngleSharp.Dom.IElement Panel(IRenderedComponent<TopicDirectory> page, string name) =>
        page.Find($"#{Toggle(page, name).GetAttribute("aria-controls")}");
}