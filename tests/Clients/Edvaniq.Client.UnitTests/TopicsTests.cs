using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// The user pastes a text, follows its analysis and sees the topics come in by themselves. The page follows the newest
// text, the topics of all texts stay visible.
public sealed class TopicsTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    // Polling runs on real time, a busy runner needs a while.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly Guid Newest = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Older = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02");
    private static readonly DateTimeOffset Created = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly TopicInfo NewTopic = new(
        Guid.NewGuid(), "Photosynthese", [new SubtopicInfo(Guid.NewGuid(), "Lichtreaktion", Newest)], Newest);

    private static readonly TopicInfo OldTopic = new(Guid.NewGuid(), "Zellatmung", [], Older);

    public TopicsTests()
    {
        Services.AddSingleton(new MaterialPolling(
            Interval: TimeSpan.FromMilliseconds(10),
            SlowInterval: TimeSpan.FromMilliseconds(10),
            SlowAfter: TimeSpan.FromMilliseconds(20),
            AnalysisLimit: TimeSpan.FromMilliseconds(40),
            TopicsLimit: TimeSpan.FromMilliseconds(40)));
    }

    // AC4: without a text the page says what to do next and offers the form right there.
    [Fact]
    public void WithoutADocument_ShowsTheNextStep()
    {
        var page = RenderPage(new FakeLearningMaterial());

        Assert.Contains("Du hast noch keinen Text eingefügt.", Status(page));
        Assert.Equal("Ersten Text einfügen", page.Find("#paste-heading").TextContent);
        Assert.NotNull(page.Find("textarea#paste-text"));
        Assert.Empty(page.FindAll(".topic-list"));
    }

    // AC2 and AC3: while the text is analyzed the page says so, and the topics come without a click.
    [Fact]
    public void Analysis_IsShown_AndTheTopicsAppearByThemselves()
    {
        var material = new FakeLearningMaterial()
            .Documents(Document(DocumentStatus.Analyzing))
            .Documents(Document(DocumentStatus.Analyzing))
            .Documents(Document(DocumentStatus.Ready))
            .Topics()
            .Topics(NewTopic);

        var page = RenderPage(material);

        Assert.Contains("„Skript Bio“ wird analysiert", Status(page));
        page.WaitForAssertion(() => Assert.Contains("Die Themen aus „Skript Bio“ sind da.", Status(page)), Wait);
        Assert.Equal(["Photosynthese", "Lichtreaktion"], page.FindAll(".topic-list li").Select(Name));
    }

    // Between Ready in Content and the topics in Knowledge lies the broker: the page waits for them.
    [Fact]
    public void TopicsLaterThanTheStatus_AreWaitedFor()
    {
        var material = new FakeLearningMaterial()
            .Documents(Document(DocumentStatus.Ready))
            .Topics()
            .Topics()
            .Topics(NewTopic);

        var page = RenderPage(material);

        Assert.Contains("Die Themen werden gerade übernommen", Status(page));
        page.WaitForAssertion(() => Assert.Contains("sind da", Status(page)), Wait);
        Assert.Equal(3, material.TopicCalls);
    }

    // Every topic and subtopic leads to a question of its own, the link names it for a screen reader.
    [Fact]
    public void EachTopic_LeadsToItsQuestion()
    {
        var material = new FakeLearningMaterial()
            .Documents(Document(DocumentStatus.Ready))
            .Topics(NewTopic);

        var page = RenderPage(material);

        var links = page.FindAll(".topic-list a.practice-link");
        Assert.Equal(
            [$"themen/{NewTopic.Id}/ueben", $"themen/{NewTopic.Subtopics[0].Id}/ueben"],
            links.Select(link => link.GetAttribute("href")));
        Assert.Equal(["Üben zu „Photosynthese“", "Üben zu „Lichtreaktion“"], links.Select(link => link.TextContent));
    }

    // The topics of earlier texts stay, the state follows the newest text.
    [Fact]
    public void EarlierTopics_StayVisible_WhileANewTextIsAnalyzed()
    {
        var material = new FakeLearningMaterial()
            .Documents(Document(DocumentStatus.Analyzing), Document(DocumentStatus.Ready, id: Older))
            .Topics(OldTopic);

        var page = RenderPage(material);

        Assert.Contains("wird analysiert", Status(page));
        Assert.Equal("Zellatmung", Name(page.Find(".topic-list li")));
        Assert.Equal("Weiteren Text einfügen", page.Find("#paste-heading").TextContent);
    }

    // Opened again after the analysis, the page shows the topics at once and asks no more.
    [Fact]
    public void Reopened_WhenAllIsDone_ShowsTheTopics_AndStopsAsking()
    {
        var material = new FakeLearningMaterial()
            .Documents(Document(DocumentStatus.Ready))
            .Topics(NewTopic, OldTopic);

        var page = RenderPage(material);

        Assert.Contains("sind da", Status(page));
        Assert.Equal(["Photosynthese", "Lichtreaktion", "Zellatmung"], page.FindAll(".topic-list li").Select(Name));
        Assert.Equal(1, material.DocumentCalls);
        Assert.Equal(1, material.TopicCalls);
    }

    [Fact]
    public void Paste_SendsTheText_AndFollowsItsAnalysis()
    {
        var material = new FakeLearningMaterial()
            .Documents()
            .Documents(Document(DocumentStatus.Ready))
            .Paste(CallOutcome<DocumentInfo>.Ok(Document(DocumentStatus.Uploaded)))
            .Topics()
            .Topics(NewTopic);
        var page = RenderPage(material);

        page.Find("#paste-title").Input("  Skript Bio  ");
        page.Find("#paste-text").Change("Die Photosynthese …");
        page.Find("form").Submit();

        Assert.Equal([("Skript Bio", "Die Photosynthese …")], material.Pasted);
        page.WaitForAssertion(() => Assert.Contains("sind da", Status(page)), Wait);
        Assert.Equal("", page.Find("#paste-text").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Paste_WithoutATitle_LeavesItToContent()
    {
        var material = new FakeLearningMaterial().Paste(CallOutcome<DocumentInfo>.Ok(Document(DocumentStatus.Uploaded)));
        var page = RenderPage(material);

        page.Find("#paste-text").Change("Text");
        page.Find("form").Submit();

        Assert.Null(Assert.Single(material.Pasted).Title);
    }

    [Theory]
    [InlineData("", "Der Text ist leer.")]
    [InlineData("   ", "Der Text ist leer.")]
    public void EmptyText_IsNotSent(string text, string hint)
    {
        var material = new FakeLearningMaterial();
        var page = RenderPage(material);

        page.Find("#paste-text").Change(text);
        page.Find("form").Submit();

        Assert.Empty(material.Pasted);
        Assert.Contains(hint, page.Find("form [role=alert]").TextContent);
    }

    // The limit counts like Content and the browser count: UTF-16 units.
    [Fact]
    public void TextAboveTheLimit_IsNotSent_AndSaysTheLimit()
    {
        var material = new FakeLearningMaterial().Paste(CallOutcome<DocumentInfo>.Ok(Document(DocumentStatus.Uploaded)));
        var page = RenderPage(material);

        page.Find("#paste-text").Change(new string('a', HttpLearningMaterial.TextMaxLength + 1));
        page.Find("form").Submit();

        Assert.Empty(material.Pasted);
        Assert.Contains("höchstens 200.000 Zeichen", page.Find("form [role=alert]").TextContent);

        page.Find("#paste-text").Change(new string('a', HttpLearningMaterial.TextMaxLength));
        page.Find("form").Submit();

        Assert.Single(material.Pasted);
    }

    [Fact]
    public void TitleAboveTheLimit_IsNotSent()
    {
        var material = new FakeLearningMaterial();
        var page = RenderPage(material);

        page.Find("#paste-title").Input(new string('t', 81));
        page.Find("#paste-text").Change("Text");
        page.Find("form").Submit();

        Assert.Empty(material.Pasted);
        Assert.Contains("höchstens 80 Zeichen", page.Find("form [role=alert]").TextContent);
    }

    // Content's own words, as they are.
    [Fact]
    public void RefusedByContent_ShowsItsMessage()
    {
        var material = new FakeLearningMaterial().Paste(new CallOutcome<DocumentInfo>(CallResult.Rejected, Messages: ["Grund von Content."]));
        var page = RenderPage(material);

        page.Find("#paste-text").Change("Text");
        page.Find("form").Submit();

        Assert.Equal("Grund von Content.", page.Find("form [role=alert] p").TextContent);
    }

    // A text that did not get through stays in the form, sending it again is the retry.
    [Fact]
    public void PasteNotThrough_KeepsTheText_AndNamesTheTrace()
    {
        var material = new FakeLearningMaterial()
            .Paste(CallOutcome<DocumentInfo>.Failed(TraceId))
            .Paste(CallOutcome<DocumentInfo>.Ok(Document(DocumentStatus.Uploaded)));
        var page = RenderPage(material);

        page.Find("#paste-text").Change("Mein Text");
        page.Find("form").Submit();

        Assert.Contains("Der Text ist nicht angekommen.", page.Find("form [role=alert]").TextContent);
        Assert.Equal(TraceId, page.Find("form [role=alert] .request-id code").TextContent);
        Assert.Equal("Mein Text", page.Find("#paste-text").GetAttribute("value"));

        page.Find("form").Submit();

        Assert.Equal(["Mein Text", "Mein Text"], material.Pasted.Select(paste => paste.Text));
        Assert.Empty(page.FindAll("form [role=alert] p"));
    }

    // AC2: a failed analysis says why and offers a retry. Content can't analyze again, so the retry sends the same text
    // as a new document.
    [Fact]
    public void FailedAnalysis_ShowsTheReason_AndRetrySendsTheTextAgain()
    {
        var failed = Document(DocumentStatus.Failed) with { FailureReason = "Die KI war gerade nicht erreichbar." };
        var material = new FakeLearningMaterial()
            .Documents()
            .Documents(failed)
            .Paste(CallOutcome<DocumentInfo>.Ok(Document(DocumentStatus.Uploaded)));
        var page = RenderPage(material);
        page.Find("#paste-text").Change("Mein Text");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("hat nicht geklappt", Status(page)), Wait);
        Assert.Contains("Die KI war gerade nicht erreichbar.", Status(page));
        var retry = page.Find("button.button:not([type=submit])");
        Assert.Equal("Erneut analysieren", retry.TextContent.Trim());

        retry.Click();

        Assert.Equal(["Mein Text", "Mein Text"], material.Pasted.Select(paste => paste.Text));
    }

    // After a reload the text is gone: the retry leads to the form.
    [Fact]
    public void FailedAnalysis_AfterAReload_LeadsToTheForm()
    {
        var material = new FakeLearningMaterial().Documents(Document(DocumentStatus.Failed));
        var page = RenderPage(material);

        Assert.Contains("hat nicht geklappt", Status(page));
        Assert.Contains("Versuch es noch einmal.", Status(page));
        var retry = page.Find("button.button:not([type=submit])");
        Assert.Equal("Text erneut einfügen", retry.TextContent.Trim());

        retry.Click();

        Assert.Empty(material.Pasted);
        Assert.Single(JSInterop.Invocations, invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalysisTakingLong_SaysSo_AndWaitingOnFollowsAgain()
    {
        var material = new FakeLearningMaterial()
            .Documents(Document(DocumentStatus.Analyzing))
            .Topics();
        var page = RenderPage(material);

        page.WaitForAssertion(() => Assert.Contains("dauert länger als gedacht", Status(page)), Wait);
        var calls = material.DocumentCalls;

        // The click may come while the page still renders the end of its polling, so it waits for its handler.
        await page.Find("button.button:not([type=submit])").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Contains("wird analysiert", Status(page));
        page.WaitForAssertion(() => Assert.True(material.DocumentCalls > calls), Wait);
    }

    [Fact]
    public void TopicsTakingLong_SayIt_WithAWayToLoadThemAgain()
    {
        var material = new FakeLearningMaterial().Documents(Document(DocumentStatus.Ready)).Topics();
        var page = RenderPage(material);

        page.WaitForAssertion(() => Assert.Contains("noch unterwegs", Status(page)), Wait);
        Assert.Equal("Themen erneut laden", page.Find("button.button:not([type=submit])").TextContent.Trim());
    }

    [Fact]
    public void LoadNotThrough_ShowsAHint_AndLoadsAgainOnRetry()
    {
        var material = new FakeLearningMaterial()
            .Documents(CallOutcome<IReadOnlyList<DocumentInfo>>.Failed(TraceId))
            .Documents(Document(DocumentStatus.Ready))
            .Topics(NewTopic);
        var page = RenderPage(material);

        Assert.Contains("Der Stand ließ sich gerade nicht laden.", Status(page));
        Assert.Equal(TraceId, page.Find("[role=status] .request-id code").TextContent);

        page.Find("button.button:not([type=submit])").Click();

        page.WaitForAssertion(() => Assert.Contains("sind da", Status(page)), Wait);
    }

    // The login ran out. Until #197 the way back is a new login, then the page again.
    [Fact]
    public void SignedOut_LeadsToANewLogin()
    {
        var page = RenderPage(new FakeLearningMaterial().Documents(CallOutcome<IReadOnlyList<DocumentInfo>>.SignedOut));

        Assert.Contains("Du bist nicht mehr angemeldet.", Status(page));
        Assert.Equal("login?returnUrl=%2Fthemen", page.Find("[role=status] a").GetAttribute("href"));
        // The form stays, so a text typed but not sent is still there (#218).
        Assert.Single(page.FindAll("form"));
        Assert.Empty(page.FindAll(".topic-list"));
    }

    // Leaving the page ends the asking.
    [Fact]
    public async Task LeavingThePage_StopsAsking()
    {
        Services.AddSingleton(new MaterialPolling(
            TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));
        var material = new FakeLearningMaterial().Documents(Document(DocumentStatus.Analyzing));
        var page = RenderPage(material);
        page.WaitForAssertion(() => Assert.True(material.DocumentCalls >= 2), Wait);

        await DisposeComponentsAsync();
        var calls = material.DocumentCalls;
        await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);

        Assert.InRange(material.DocumentCalls, calls, calls + 1);
    }

    // Titles and topics come from the user and the analysis: text, never markup.
    [Fact]
    public void TitlesAndTopics_AreText()
    {
        var material = new FakeLearningMaterial()
            .Documents(Document(DocumentStatus.Ready) with { Title = "<b>fett</b>" })
            .Topics(new TopicInfo(Guid.NewGuid(), "<script>x()</script>", [], Newest));

        var page = RenderPage(material);

        Assert.Empty(page.FindAll("[role=status] b"));
        Assert.Empty(page.FindAll("script"));
        Assert.Equal("<script>x()</script>", Name(page.Find(".topic-list li")));
    }

    [Fact]
    public void WhilePrerendering_NothingIsAsked_AndThePageSaysItIsLoading()
    {
        var material = new FakeLearningMaterial();
        Services.AddSingleton<ILearningMaterial>(material);
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var page = Render<Topics>();

        Assert.Equal("Deine Themen", page.Find("h1").TextContent);
        Assert.Equal("Wird geladen …", page.Find("[role=status]").TextContent.Trim());
        Assert.Equal(0, material.DocumentCalls);
    }

    private IRenderedComponent<Topics> RenderPage(FakeLearningMaterial material)
    {
        Services.AddSingleton<ILearningMaterial>(material);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Topics>();
    }

    private static string Status(IRenderedComponent<Topics> page) => page.Find("[role=status]").TextContent;

    // The topic's own name, without its subtopics.
    private static string Name(AngleSharp.Dom.IElement item) =>
        item.ChildNodes.First(node => node.NodeType == AngleSharp.Dom.NodeType.Text && node.TextContent.Trim().Length > 0)
            .TextContent.Trim();

    private static DocumentInfo Document(DocumentStatus status, Guid? id = null) =>
        new(id ?? Newest, "Skript Bio", status, Created);
}