using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #307: what is left of today's pages at the upload field, and the sentences of the daily limits and of a full learning
// goal, without anything technical.
public sealed class QuotaTests : BunitContext
{
    // Content's sentences (PageQuota, GenerationQuota, Document.GoalFull), as they come in the problem's title.
    private const string PagesTooMany =
        "Dein Tageskontingent reicht für dieses Dokument nicht: 3 Seiten, frei ist heute noch 1. Es wird am 16.11. um 04:00 Uhr zurückgesetzt.";

    private const string PagesUsedUp = "Dein Tageskontingent an Seiten ist aufgebraucht. Es wird am 16.11. um 04:00 Uhr zurückgesetzt.";

    private const string GenerationsUsedUp = "Dein Tageskontingent an Generierungen ist aufgebraucht. Es wird am 16.11. um 04:00 Uhr zurückgesetzt.";

    private const string GoalFull =
        "Dieses Lernziel hat schon 10 Dokumente. Lösche eines, das du nicht mehr brauchst, oder lege ein neues Lernziel an.";

    private static readonly DateTimeOffset Created = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    public QuotaTests()
    {
        Services.AddSingleton(new MaterialPolling(
            Interval: TimeSpan.FromMilliseconds(10),
            SlowInterval: TimeSpan.FromMilliseconds(10),
            SlowAfter: TimeSpan.FromMilliseconds(20),
            AnalysisLimit: TimeSpan.FromMilliseconds(200)));
    }

    // Read out with the file field, so a screen reader hears it on focus, not on each reload.
    [Fact]
    public void PagesLeft_AreShownAtTheUploadField()
    {
        var page = RenderPage(new FakeLearningMaterial(), new FakeMaterialQuota().Answer(42));

        Assert.Equal("Heute sind noch 42 von 200 Seiten frei.", page.Find("#upload-quota").TextContent);
        Assert.Equal("upload-quota upload-hint upload-privacy", page.Find("input#upload-file").GetAttribute("aria-describedby"));
        Assert.Null(page.Find("#upload-quota").Closest("[role=alert], [role=status], [aria-live]"));
    }

    [Fact]
    public void OnePageLeft_IsSingular()
    {
        var page = RenderPage(new FakeLearningMaterial(), new FakeMaterialQuota().Answer(1));

        Assert.Equal("Heute ist noch 1 von 200 Seiten frei.", page.Find("#upload-quota").TextContent);
    }

    [Fact]
    public void NoPagesLeft_SaysWhenItGoesOn()
    {
        var page = RenderPage(new FakeLearningMaterial(), new FakeMaterialQuota().Answer(0));

        Assert.Equal(FeedbackTexts.Upload.NoPagesLeft, page.Find("#upload-quota").TextContent);
        Assert.Contains("04:00 Uhr", FeedbackTexts.Upload.NoPagesLeft);
    }

    // Without an answer the count is just missing: the upload works anyway and Content says why if it refuses. A lost
    // login is the shell's to report (SessionWatch).
    [Theory]
    [InlineData(CallResult.Failed)]
    [InlineData(CallResult.SignedOut)]
    public void QuotaNotThrough_ShowsNothing_AndTheUploadStays(CallResult result)
    {
        var page = RenderPage(new FakeLearningMaterial(), new FakeMaterialQuota().Answer(new CallOutcome<MaterialQuota>(result)));

        Assert.Empty(page.FindAll("#upload-quota"));
        Assert.Equal("upload-hint upload-privacy", page.Find("input#upload-file").GetAttribute("aria-describedby"));
        Assert.Empty(page.FindAll(".connection-problem"));
        Assert.Empty(UploadAlert(page).Children);
        Assert.NotNull(page.Find("#upload-heading ~ button"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoPagesPerDay_ShowsNothing(int perDay)
    {
        var page = RenderPage(new FakeLearningMaterial(), new FakeMaterialQuota().Answer(0, perDay));

        Assert.Empty(page.FindAll("#upload-quota"));
    }

    // The worker charges the pages once it has read the file, so the count is asked again when the analysis is over.
    [Fact]
    public async Task AfterTheAnalysis_TheCountIsAskedAgain()
    {
        var material = new FakeLearningMaterial()
            .Documents()
            .Documents(Document(DocumentStatus.Ready))
            .Upload(CallOutcome<DocumentInfo>.Ok(Document(DocumentStatus.Uploaded)));
        var quotas = new FakeMaterialQuota().Answer(42).Answer(39);
        var page = RenderPage(material, quotas);
        page.Confirm();

        Choose(page);
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal("Heute sind noch 39 von 200 Seiten frei.", page.Find("#upload-quota").TextContent), TimeSpan.FromSeconds(10));
        Assert.Equal(2, quotas.Calls);
    }

    // The daily limits name why and when it goes on, as a warning in the live region, without a trace.
    [Theory]
    [InlineData(PagesUsedUp)]
    [InlineData(GenerationsUsedUp)]
    public async Task UploadLimited_ShowsContentsSentence_AndAsksTheCountAgain(string sentence)
    {
        var material = new FakeLearningMaterial().Upload(new CallOutcome<DocumentInfo>(CallResult.Limited, Messages: [sentence]));
        var quotas = new FakeMaterialQuota().Answer(5).Answer(0);
        var page = RenderPage(material, quotas);
        page.Confirm();

        Choose(page);
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Equal(sentence, UploadAlert(page).QuerySelector(".notice-warning .notice-title")!.TextContent);
        Assert.Empty(page.FindAll(".request-id"));
        Assert.Equal(FeedbackTexts.Upload.NoPagesLeft, page.Find("#upload-quota").TextContent);
    }

    [Theory]
    [InlineData(PagesTooMany)]
    [InlineData(GenerationsUsedUp)]
    public async Task PasteLimited_ShowsContentsSentence_AndKeepsTheText(string sentence)
    {
        var material = new FakeLearningMaterial().Paste(new CallOutcome<DocumentInfo>(CallResult.Limited, Messages: [sentence]));
        var page = RenderPage(material, new FakeMaterialQuota().Answer(1));
        page.Confirm();

        page.Find("#paste-text").Change("Ein langer Text.");
        await page.Find(".paste-form").SubmitAsync();

        var notice = PasteAlert(page).QuerySelector(".notice-warning")!;
        Assert.Contains(sentence, notice.TextContent);
        Assert.Empty(page.FindAll(".request-id"));
        Assert.Equal("Ein langer Text.", page.Find("#paste-text").GetAttribute("value"));
    }

    // #277: a full learning goal says what to do instead, no "didn't get through" and no trace.
    [Fact]
    public async Task GoalFull_OnPaste_SaysWhatToDo()
    {
        var material = new FakeLearningMaterial().Paste(new CallOutcome<DocumentInfo>(CallResult.Rejected, Messages: [GoalFull]));
        var page = RenderPage(material, new FakeMaterialQuota().Answer(42));
        page.Confirm();

        page.Find("#paste-text").Change("Text");
        await page.Find(".paste-form").SubmitAsync();

        Assert.Contains(GoalFull, PasteAlert(page).TextContent);
        Assert.DoesNotContain(FeedbackTexts.Material.TextNotThrough, PasteAlert(page).TextContent);
        Assert.Empty(page.FindAll(".request-id"));
    }

    [Fact]
    public async Task GoalFull_OnUpload_SaysWhatToDo()
    {
        var material = new FakeLearningMaterial().Upload(new CallOutcome<DocumentInfo>(CallResult.Rejected, Messages: [GoalFull]));
        var page = RenderPage(material, new FakeMaterialQuota().Answer(42));
        page.Confirm();

        Choose(page);
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Equal(GoalFull, UploadAlert(page).QuerySelector(".notice-title")!.TextContent);
        Assert.DoesNotContain(FeedbackTexts.Upload.NotThrough, UploadAlert(page).TextContent);
        Assert.DoesNotContain(FeedbackTexts.Connection.Advice, UploadAlert(page).TextContent);
        Assert.Empty(page.FindAll(".request-id"));
    }

    private static void Choose(IRenderedComponent<Topics> page) =>
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("Ein kurzer Text.", "Notizen.txt"));

    private IRenderedComponent<Topics> RenderPage(FakeLearningMaterial material, FakeMaterialQuota quotas)
    {
        Services.AddSingleton<ILearningMaterial>(material);
        Services.AddSingleton<IMaterialQuota>(quotas);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Topics>();
    }

    private static AngleSharp.Dom.IElement UploadAlert(IRenderedComponent<Topics> page) =>
        page.Find("section[aria-labelledby=upload-heading] [role=alert]");

    private static AngleSharp.Dom.IElement PasteAlert(IRenderedComponent<Topics> page) =>
        page.Find(".paste-form [role=alert]");

    private static DocumentInfo Document(DocumentStatus status) =>
        new(Guid.Parse("6b7c8d9e-0f1a-4b2c-8d3e-4f5a6b7c8d9e"), "Notizen", status, Created, Source: DocumentSource.File);
}