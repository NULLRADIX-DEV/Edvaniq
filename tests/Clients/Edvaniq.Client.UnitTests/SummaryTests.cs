using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #346: the summary of a topic in three lengths. Switching shows the right one, a summary still made shows a loading
// state, and Content's sentences tell what holds it up.
public sealed class SummaryTests : BunitContext
{
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    public SummaryTests()
    {
        Services.AddSingleton(new SummaryPolling(Interval: TimeSpan.FromMilliseconds(10), Limit: TimeSpan.FromMilliseconds(60)));
    }

    // Standard is asked for first, the buttons say which length is shown.
    [Fact]
    public void Opening_ShowsTheStandardSummary()
    {
        var summaries = new FakeTopicSummaries().OnRequest(Ready(SummaryLength.Standard, "Standardtext."));
        var page = RenderPage(summaries);

        Assert.Equal("Zusammenfassung zu „Photosynthese“", page.Find("h1").TextContent);
        Assert.Equal(["Kurz", "Standard", "Detailliert"], page.FindAll(".summary-length").Select(button => button.TextContent.Trim()));
        Assert.Equal(["false", "true", "false"], page.FindAll(".summary-length").Select(button => button.GetAttribute("aria-pressed")));
        Assert.Equal("Länge", page.Find("[role=group]").GetAttribute("aria-label"));
        Assert.Equal(["Standardtext."], Paragraphs(page));
        Assert.Equal([SummaryLength.Standard], summaries.Requested);
    }

    // AK1: each length shows its own summary. One loaded before comes from the page, without a second POST.
    [Fact]
    public async Task SwitchingLength_ShowsItsSummary_AndAsksEachLengthOnce()
    {
        var summaries = new FakeTopicSummaries()
            .OnRequest(Ready(SummaryLength.Standard, "Standardtext."))
            .OnRequest(Ready(SummaryLength.Short, "Kurztext."))
            .OnRequest(Ready(SummaryLength.Detailed, "Langer Text."));
        var page = RenderPage(summaries);

        await Button(page, "Kurz").ClickAsync(new());
        Assert.Equal(["Kurztext."], Paragraphs(page));
        Assert.Equal("true", Button(page, "Kurz").GetAttribute("aria-pressed"));

        await Button(page, "Detailliert").ClickAsync(new());
        Assert.Equal(["Langer Text."], Paragraphs(page));

        await Button(page, "Kurz").ClickAsync(new());
        await Button(page, "Standard").ClickAsync(new());
        Assert.Equal(["Standardtext."], Paragraphs(page));

        Assert.Equal([SummaryLength.Standard, SummaryLength.Short, SummaryLength.Detailed], summaries.Requested);
    }

    // AK2: while it is made the page says so in the polite live region and asks until it is there.
    [Fact]
    public void Pending_ShowsTheLoadingState_UntilItIsReady()
    {
        var summaries = new FakeTopicSummaries()
            .OnRequest(Pending(SummaryLength.Standard))
            .OnGet(Pending(SummaryLength.Standard))
            .OnGet(Ready(SummaryLength.Standard, "Fertig."));
        var page = RenderPage(summaries);

        var status = page.Find("[role=status]");
        Assert.Contains(FeedbackTexts.Summary.Writing, status.TextContent);
        Assert.Null(status.GetAttribute("aria-live") is "assertive" ? "assertive" : null);
        Assert.Empty(page.FindAll(".summary-text"));

        page.WaitForAssertion(() => Assert.Equal(["Fertig."], Paragraphs(page)), Wait);
        Assert.Contains(FeedbackTexts.Summary.Ready, page.Find("[role=status]").TextContent);
        Assert.Single(summaries.Requested);
        Assert.True(summaries.Read.Count >= 2);
    }

    // The page stops asking after its limit and offers to look again, which follows the summary once more.
    [Fact]
    public async Task LongPending_StopsAtTheLimit_AndChecksAgainOnRequest()
    {
        var summaries = new FakeTopicSummaries()
            .OnRequest(Pending(SummaryLength.Standard))
            .OnGet(Pending(SummaryLength.Standard));
        var page = RenderPage(summaries);

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Summary.Slow, page.Find("[role=status]").TextContent), Wait);
        var reads = summaries.Read.Count;
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(reads, summaries.Read.Count);

        summaries.OnGet(Ready(SummaryLength.Standard, "Doch noch."));
        summaries.OnGet(Ready(SummaryLength.Standard, "Doch noch."));
        await page.FindAll("button").Single(button => button.TextContent.Trim() == FeedbackTexts.Summary.CheckAgain).ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal(["Doch noch."], Paragraphs(page)), Wait);
        Assert.Single(summaries.Requested);
    }

    // Leaving the page ends the asking.
    [Fact]
    public void LeavingThePage_StopsAsking()
    {
        var summaries = new FakeTopicSummaries()
            .OnRequest(Pending(SummaryLength.Standard))
            .OnGet(Pending(SummaryLength.Standard));
        Services.AddSingleton(new SummaryPolling(Interval: TimeSpan.FromMilliseconds(10), Limit: TimeSpan.FromMinutes(1)));
        var page = RenderPage(summaries);
        page.WaitForAssertion(() => Assert.NotEmpty(summaries.Read), Wait);

        page.FindComponent<SummaryView>().Instance.Dispose();
        var reads = summaries.Read.Count;
        Thread.Sleep(100);

        Assert.InRange(summaries.Read.Count, reads, reads + 1);
    }

    // A failed one names Content's reason, or says it plainly without one, and "Noch einmal" asks Content to try again.
    [Theory]
    [InlineData("Die KI hat diese Zusammenfassung abgelehnt.", "Die KI hat diese Zusammenfassung abgelehnt.")]
    [InlineData(null, FeedbackTexts.Summary.FailedHint)]
    public async Task Failed_SaysWhy_AndTriesAgain(string? reason, string shown)
    {
        var summaries = new FakeTopicSummaries()
            .OnRequest(Failed(SummaryLength.Standard, reason))
            .OnRequest(Ready(SummaryLength.Standard, "Jetzt geht es."));
        var page = RenderPage(summaries);

        var status = page.Find("[role=status]").TextContent;
        Assert.Contains(FeedbackTexts.Summary.Failed, status);
        Assert.Contains(shown, status);

        await page.FindAll("button").Single(button => button.TextContent.Trim() == FeedbackTexts.Summary.TryAgain).ClickAsync(new());

        Assert.Equal(["Jetzt geht es."], Paragraphs(page));
        Assert.Equal(2, summaries.Requested.Count);
    }

    [Fact]
    public void NoMaterial_SaysSo_AndLeadsToTheMaterial()
    {
        const string sentence = "Zu diesem Thema gibt es noch kein Material, aus dem eine Zusammenfassung entstehen kann.";
        var page = RenderPage(new FakeTopicSummaries()
            .OnRequest(SummaryLength.Standard, new CallOutcome<TopicSummary>(CallResult.NotReady, Messages: [sentence])));

        Assert.Contains(sentence, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll("[role=status] .notice-error"));
        var link = page.FindAll("a").Single(a => a.TextContent.Trim() == FeedbackTexts.Summary.AddMaterial);
        Assert.Equal("themen", link.GetAttribute("href"));
    }

    [Theory]
    [InlineData("Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.")]
    [InlineData("Zusammenfassungen sind gerade pausiert. Versuch es später noch einmal.")]
    public void LimitedOrPaused_ShowsContentsSentence_AsAWarning(string sentence)
    {
        var page = RenderPage(new FakeTopicSummaries()
            .OnRequest(SummaryLength.Standard, new CallOutcome<TopicSummary>(CallResult.Limited, Messages: [sentence])));

        Assert.Equal(sentence, page.Find("[role=status] .notice-warning p").TextContent);
        Assert.Empty(page.FindAll(".request-id"));
        Assert.Contains(page.FindAll("button"), button => button.TextContent.Trim() == FeedbackTexts.Summary.TryAgain);
    }

    [Fact]
    public void TopicGone_LeadsBackToTheTopics()
    {
        var page = RenderPage(new FakeTopicSummaries().OnRequest(SummaryLength.Standard, new CallOutcome<TopicSummary>(CallResult.NotFound)));

        Assert.Contains(FeedbackTexts.Summary.TopicGone, page.Find("[role=status]").TextContent);
        Assert.Contains(page.FindAll("a"), a => a.TextContent.Trim() == FeedbackTexts.Summary.BackToTopics);
    }

    // No connection: the shared connection hint with the trace and a retry.
    [Fact]
    public async Task NotThrough_ShowsTheConnectionProblem_AndRetries()
    {
        const string trace = "4bf92f3577b34da6a3ce929d0e0e4736";
        var summaries = new FakeTopicSummaries()
            .OnRequest(SummaryLength.Standard, CallOutcome<TopicSummary>.Failed(trace))
            .OnRequest(Ready(SummaryLength.Standard, "Da."));
        var page = RenderPage(summaries);

        Assert.Contains(FeedbackTexts.Summary.LoadFailed, page.Find("[role=status]").TextContent);
        Assert.Contains(trace, page.Find("[role=status]").TextContent);

        await page.FindAll("button").Single(button => button.TextContent.Trim() == FeedbackTexts.Connection.Retry).ClickAsync(new());

        Assert.Equal(["Da."], Paragraphs(page));
    }

    // AI text is only ever text: markup in it shows as written, each line is its own paragraph.
    [Fact]
    public void Text_IsShownAsText_LineByLine()
    {
        var page = RenderPage(new FakeTopicSummaries()
            .OnRequest(Ready(SummaryLength.Standard, "Erster <b>Absatz</b>.\n\n<script>alert(1)</script>\nDritter.")));

        Assert.Equal(["Erster <b>Absatz</b>.", "<script>alert(1)</script>", "Dritter."], Paragraphs(page));
        Assert.Empty(page.FindAll(".summary-text b"));
        Assert.Empty(page.FindAll(".summary-text script"));
    }

    // While a length is asked for, switching away and back doesn't ask it a second time.
    [Fact]
    public async Task SwitchingWhileAsking_DoesNotAskTwice()
    {
        var summaries = new FakeTopicSummaries()
            .OnRequest(Ready(SummaryLength.Standard, "Standardtext."))
            .OnRequest(Ready(SummaryLength.Short, "Kurztext."));
        summaries.RequestGate = new TaskCompletionSource();
        var page = RenderPage(summaries);

        // The handlers wait for the gate, so the clicks are awaited only once it is open.
        var toShort = Button(page, "Kurz").ClickAsync(new());
        var back = Button(page, "Standard").ClickAsync(new());
        Assert.Contains(FeedbackTexts.Summary.Loading, page.Find("[role=status]").TextContent);
        summaries.RequestGate.SetResult();
        await Task.WhenAll(toShort, back).WaitAsync(Wait, Xunit.TestContext.Current.CancellationToken);

        page.WaitForAssertion(() => Assert.Equal(["Standardtext."], Paragraphs(page)), Wait);
        Assert.Equal([SummaryLength.Standard, SummaryLength.Short], summaries.Requested);
    }

    private IRenderedComponent<Summary> RenderPage(FakeTopicSummaries summaries)
    {
        Services.AddSingleton<ITopicSummaries>(summaries);
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(new TopicInfo(Topic, "Photosynthese", [])));
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Summary>(parameters => parameters.Add(p => p.TopicId, Topic));
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Summary> page, string label) =>
        page.FindAll(".summary-length").Single(button => button.TextContent.Trim() == label);

    private static IEnumerable<string> Paragraphs(IRenderedComponent<Summary> page) =>
        page.FindAll(".summary-text p").Select(paragraph => paragraph.TextContent);

    private static TopicSummary Ready(SummaryLength length, string text) =>
        new(Topic, length, SummaryStatus.Ready, text, null, true, []);

    private static TopicSummary Pending(SummaryLength length) => new(Topic, length, SummaryStatus.Pending, null, null, true, []);

    private static TopicSummary Failed(SummaryLength length, string? reason) => new(Topic, length, SummaryStatus.Failed, null, reason, true, []);
}