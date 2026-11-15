using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #351: every question and summary says it is AI text that can be wrong, names the document it comes from, and opens the
// place in the original. A question shows its source only with the feedback: page and passage often hold the answer.
public sealed class SourceTests : BunitContext
{
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Attempt = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Pdf = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid Pasted = Guid.Parse("2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e");
    private static readonly Guid Word = Guid.Parse("3c4d5e6f-7a8b-4c9d-0e1f-2a3b4c5d6e7f");
    private static readonly Guid Section = Guid.Parse("4d5e6f7a-8b9c-4d0e-1f2a-3b4c5d6e7f80");
    private static readonly DateTimeOffset Created = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeSources sources = new();

    public SourceTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<TimeProvider>(new ManualTime());
        Services.AddSingleton<ISources>(sources);
        Services.AddSingleton(new SummaryPolling(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1)));
    }

    // AK1/AK4: label and hint stand at the question from the start, in words and named as a group. The source does not:
    // it might give the answer away.
    [Fact]
    public void Question_HasLabelAndHint_ButNoSourceBeforeTheAnswer()
    {
        var page = RenderQuiz(Question(Pdf), Documents(Doc(Pdf, "Skript Biologie")));

        var note = page.Find(".quiz-form .ai-note");
        Assert.Equal("group", note.GetAttribute("role"));
        Assert.Equal(FeedbackTexts.Sources.NoteLabel, note.GetAttribute("aria-label"));
        Assert.Equal(FeedbackTexts.Sources.AiGenerated, note.QuerySelector(".ai-label")!.TextContent);
        Assert.Equal(FeedbackTexts.Sources.MayErr, note.QuerySelector(".ai-hint")!.TextContent);
        Assert.Empty(page.FindAll(".source"));
        Assert.Empty(sources.Asked);
    }

    // AK1: after the answer the feedback names the document, the place loads only when asked for.
    [Fact]
    public void Feedback_NamesTheDocument_AndOpensThePdfAtItsPage()
    {
        sources.Answer(new SectionSource(Pdf, Section, "Skript Biologie", SourceFormats.Pdf, true, Page: 3, PageTo: 4));
        var page = RenderQuiz(Question(Pdf), Documents(Doc(Pdf, "Skript Biologie", fromFile: true)));
        Answer(page);

        var source = page.Find(".quiz-feedback .source");
        Assert.Equal(FeedbackTexts.Sources.Heading, page.Find(".quiz-source-heading").TextContent);
        Assert.Equal("Skript Biologie", source.QuerySelector(".source-title")!.TextContent);
        Assert.Empty(sources.Asked);

        var toggle = source.QuerySelector(".source-toggle")!;
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Stelle ansehen in „Skript Biologie“", Squeeze(toggle.TextContent));
        page.Find(".source-toggle").Click();

        Assert.Equal([Section], sources.Asked);
        Assert.Equal("true", page.Find(".source-toggle").GetAttribute("aria-expanded"));
        Assert.Equal("S. 3–4", page.Find(".source-place").TextContent);
        var link = page.Find("a.source-open");
        Assert.Equal($"api/content/documents/{Pdf}/file#page=3", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener", link.GetAttribute("rel"));
        Assert.False(link.HasAttribute("download"));
        Assert.Equal("Original öffnen „Skript Biologie“, S. 3 (öffnet in neuem Tab)", Squeeze(link.TextContent));
        Assert.Empty(page.FindAll("iframe"));
    }

    // AK3: a text opens at the passage: the window around the section, the section marked, cut text shown as "…". The
    // text stays text, also where it looks like markup.
    [Fact]
    public void PastedText_ShowsThePassage_Marked_AndCut()
    {
        const string text = "Davor. <b>Die Lichtreaktion</b> läuft in den Thylakoiden ab. Danach.";
        var start = text.IndexOf("<b>", StringComparison.Ordinal);
        var length = text.IndexOf(" Danach", StringComparison.Ordinal) - start;
        sources.Answer(new SectionSource(Pasted, Section, "Mitschrift", null, false, Text: text, Start: start, Length: length,
            TruncatedBefore: true, TruncatedAfter: true, Position: 1));
        var page = RenderQuiz(Question(Pasted), Documents(Doc(Pasted, "Mitschrift")));
        Answer(page);

        page.Find(".source-toggle").Click();

        Assert.Equal("Abschnitt 2", page.Find(".source-place").TextContent);
        var window = page.Find("blockquote.source-window");
        Assert.Equal("… Davor. <b>Die Lichtreaktion</b> läuft in den Thylakoiden ab. Danach. …", window.TextContent);
        Assert.Equal("<b>Die Lichtreaktion</b> läuft in den Thylakoiden ab.", window.QuerySelector("mark")!.TextContent);
        Assert.Empty(window.QuerySelectorAll("b"));
        Assert.Empty(page.FindAll("a.source-open"));
    }

    // Without a position from Content it still opens, as "Textstelle".
    [Fact]
    public void Passage_WithoutPosition_IsATextPassage()
    {
        sources.Answer(new SectionSource(Pasted, Section, "Mitschrift", null, false, Text: "Nur das.", Start: 0, Length: 8));
        var page = RenderQuiz(Question(Pasted), Documents(Doc(Pasted, "Mitschrift")));
        Answer(page);

        page.Find(".source-toggle").Click();

        Assert.Equal(FeedbackTexts.Sources.Passage, page.Find(".source-place").TextContent);
        Assert.Equal("Nur das.", page.Find("blockquote.source-window").TextContent);
    }

    // A Word file has its passage and the file to save, no new tab.
    [Fact]
    public void WordFile_ShowsThePassage_AndOffersTheDownload()
    {
        sources.Answer(new SectionSource(Word, Section, "Referat", SourceFormats.Docx, true, Text: "Text.", Start: 0, Length: 5, Position: 0));
        var page = RenderQuiz(Question(Word), Documents(Doc(Word, "Referat", fromFile: true)));
        Answer(page);

        page.Find(".source-toggle").Click();

        var link = page.Find("a.source-open");
        Assert.Equal($"api/content/documents/{Word}/file", link.GetAttribute("href"));
        Assert.True(link.HasAttribute("download"));
        Assert.Null(link.GetAttribute("target"));
        Assert.StartsWith(FeedbackTexts.Sources.Download, Squeeze(link.TextContent));
    }

    // AK2: what comes from text the AI wrote itself says so, and has nothing to open.
    [Fact]
    public void Synthetic_IsLabeledWithoutMaterial_AndHasNothingToOpen()
    {
        var page = RenderQuiz(Question(Pdf), Documents(Doc(Pdf, "Erklärtext", origin: DocumentOrigins.Synthetic)));

        page.WaitForAssertion(() => Assert.Equal(FeedbackTexts.Sources.AiWithoutMaterial, page.Find(".quiz-form .ai-label").TextContent));
        Answer(page);

        Assert.Equal("Erklärtext", page.Find(".source-title").TextContent);
        Assert.Empty(page.FindAll(".source-toggle"));
        Assert.Empty(page.FindAll("a.source-open"));
    }

    // Demo material and an origin this app doesn't know yet are real material: a source to open, never "without material".
    [Theory]
    [InlineData(DocumentOrigins.Demo, FeedbackTexts.Sources.AiFromDemo)]
    [InlineData("borrowed", FeedbackTexts.Sources.AiGenerated)]
    [InlineData(null, FeedbackTexts.Sources.AiGenerated)]
    public void DemoAndUnknownOrigins_CountAsMaterial(string? origin, string label)
    {
        var page = RenderQuiz(Question(Pdf), Documents(Doc(Pdf, "Skript", origin: origin)));

        page.WaitForAssertion(() => Assert.Equal(label, page.Find(".quiz-form .ai-label").TextContent));
        Answer(page);

        Assert.NotNull(page.Find(".source-toggle"));
    }

    // A source deleted meanwhile says so, both when its document is gone and when Content no longer has the section.
    [Fact]
    public void DocumentGone_SaysTheSourceIsGone()
    {
        var page = RenderQuiz(Question(Pdf), Documents());
        Answer(page);

        Assert.Equal(FeedbackTexts.Sources.Gone, page.Find(".source-gone").TextContent);
        Assert.Empty(page.FindAll(".source-toggle"));
    }

    [Fact]
    public void SectionGone_SaysTheSourceIsGone()
    {
        sources.Answer(Section, new CallOutcome<SectionSource>(CallResult.NotFound));
        var page = RenderQuiz(Question(Pdf), Documents(Doc(Pdf, "Skript")));
        Answer(page);

        page.Find(".source-toggle").Click();

        Assert.Equal(FeedbackTexts.Sources.Gone, page.Find(".source-panel .source-gone").TextContent);
    }

    [Fact]
    public void SectionNotThrough_ShowsTheConnectionProblem_AndTriesAgain()
    {
        sources.Answer(Section, CallOutcome<SectionSource>.Failed("4bf92f3577b34da6a3ce929d0e0e4736"))
            .Answer(new SectionSource(Pdf, Section, "Skript", SourceFormats.Pdf, true, Page: 7));
        var page = RenderQuiz(Question(Pdf), Documents(Doc(Pdf, "Skript", fromFile: true)));
        Answer(page);

        page.Find(".source-toggle").Click();
        Assert.Contains(FeedbackTexts.Sources.LoadFailed, page.Find(".source-panel [role=status]").TextContent);

        page.FindAll(".source-panel button").Single(button => button.TextContent.Trim() == FeedbackTexts.Connection.Retry).Click();

        Assert.Equal($"api/content/documents/{Pdf}/file#page=7", page.Find("a.source-open").GetAttribute("href"));
    }

    // The documents are asked once per page, however many questions and sources follow.
    [Fact]
    public void Documents_AreAskedOncePerPage()
    {
        var material = Documents(Doc(Pdf, "Skript"));
        var page = RenderQuiz(Question(Pdf), material);
        Answer(page);
        page.Find(".quiz-actions button").Click();
        Answer(page);

        Assert.Equal(1, material.DocumentCalls);
    }

    // A question without a source (an older Assessment) still has label and hint, and no source.
    [Fact]
    public void QuestionWithoutSource_HasLabelAndHint_Only()
    {
        var page = RenderQuiz(Question(Pdf) with { Source = null }, Documents());
        Answer(page);

        Assert.NotNull(page.Find(".ai-note"));
        Assert.Empty(page.FindAll(".source"));
        Assert.Empty(page.FindAll(".quiz-source-heading"));
    }

    // AK1: a summary has the label, the hint and its sources, each to open.
    [Fact]
    public void Summary_ShowsLabelHintAndSources()
    {
        sources.Answer(new SectionSource(Pdf, Section, "Skript Biologie", SourceFormats.Pdf, true, Page: 3, PageTo: 4));
        var page = RenderSummary(
            [new SummarySource(Pdf, "Skript Biologie", 3, 4, DocumentOrigins.Material, Section), new SummarySource(Pasted, "Mitschrift", null, null, DocumentOrigins.Material)],
            Documents(Doc(Pdf, "Skript Biologie", fromFile: true), Doc(Pasted, "Mitschrift")));

        var meta = page.Find(".summary-meta");
        Assert.Equal(FeedbackTexts.Sources.AiGenerated, meta.QuerySelector(".ai-label")!.TextContent);
        Assert.Equal(FeedbackTexts.Sources.MayErr, meta.QuerySelector(".ai-hint")!.TextContent);
        Assert.Equal(FeedbackTexts.Sources.HeadingMany, meta.QuerySelector("h2")!.TextContent);
        var items = page.FindAll(".summary-sources li");
        Assert.Equal("Skript Biologie, S. 3–4", Squeeze(items[0].QuerySelector(".source-line")!.TextContent));

        // A pasted text without a section to open says so instead of a dead link.
        Assert.Equal("Mitschrift", items[1].QuerySelector(".source-title")!.TextContent);
        Assert.Equal(FeedbackTexts.Sources.CannotOpen, items[1].QuerySelector(".source-hint")!.TextContent);

        items[0].QuerySelector(".source-toggle")!.Click();
        Assert.Equal($"api/content/documents/{Pdf}/file#page=3", page.Find(".summary-sources a.source-open").GetAttribute("href"));
    }

    // A PDF source without a section still opens at its first page, a file as a whole.
    [Fact]
    public void SummarySource_WithoutSection_OpensThePdfAtItsPage_OrTheFile()
    {
        var page = RenderSummary(
            [new SummarySource(Pdf, "Skript", 5, 6, DocumentOrigins.Material), new SummarySource(Word, "Referat", null, null, DocumentOrigins.Material)],
            Documents(Doc(Pdf, "Skript", fromFile: true), Doc(Word, "Referat", fromFile: true)));

        var links = page.FindAll(".summary-sources a.source-open").Select(link => link.GetAttribute("href"));
        Assert.Equal([$"api/content/documents/{Pdf}/file#page=5", $"api/content/documents/{Word}/file"], links);
    }

    // AK2 for summaries: without material behind it, or with none at all, the label says so.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Summary_WithoutMaterial_SaysSo(bool syntheticSource)
    {
        IReadOnlyList<SummarySource> list = syntheticSource ? [new SummarySource(Pdf, "Erklärtext", null, null, DocumentOrigins.Synthetic)] : [];
        var page = RenderSummary(list, Documents(Doc(Pdf, "Erklärtext", origin: DocumentOrigins.Synthetic)));

        Assert.Equal(FeedbackTexts.Sources.AiWithoutMaterial, page.Find(".summary-meta .ai-label").TextContent);
    }

    private IRenderedComponent<Practice> RenderQuiz(AskedQuestion question, FakeLearningMaterial material)
    {
        Services.AddSingleton<IQuiz>(new FakeQuiz().Ask(question).Answer(new GradedAnswer(Attempt, 1, true, 1, "Weil.", 1000)));
        Services.AddSingleton<ILearningMaterial>(material.Topics(new TopicInfo(Topic, "Photosynthese", [])));
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));
    }

    private IRenderedComponent<Summary> RenderSummary(IReadOnlyList<SummarySource> list, FakeLearningMaterial material)
    {
        Services.AddSingleton<ITopicSummaries>(new FakeTopicSummaries()
            .OnRequest(new TopicSummary(Topic, SummaryLength.Standard, SummaryStatus.Ready, "Text.", null, true, list)));
        Services.AddSingleton<ILearningMaterial>(material.Topics(new TopicInfo(Topic, "Photosynthese", [])));
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Summary>(parameters => parameters.Add(p => p.TopicId, Topic));
    }

    private static void Answer(IRenderedComponent<Practice> page)
    {
        page.Find("#option-1").Change("1");
        page.Find("form").Submit();
    }

    private static AskedQuestion Question(Guid document) =>
        new(Attempt, Topic, Guid.NewGuid(), "Wo?", ["A", "B"], 1, QuestionTypes.SingleChoice, new QuestionSource(document, Section, "model", "questions-3"));

    private static FakeLearningMaterial Documents(params DocumentInfo[] list) => new FakeLearningMaterial().Documents(list);

    private static DocumentInfo Doc(Guid id, string title, bool fromFile = false, string? origin = DocumentOrigins.Material) =>
        new(id, title, DocumentStatus.Ready, Created, Source: fromFile ? DocumentSource.File : DocumentSource.Text, Origin: origin);

    private static string Squeeze(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}