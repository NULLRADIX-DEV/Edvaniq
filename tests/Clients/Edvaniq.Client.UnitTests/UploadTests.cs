using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// The upload field on the topics page. Size and type are checked here only to spare the way up, Content decides. A file
// is followed afterwards like a pasted text, the worker reads it first.
public sealed class UploadTests : BunitContext
{
    private static readonly Guid Uploaded = Guid.Parse("5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d");
    private static readonly DateTimeOffset Created = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    public UploadTests()
    {
        Services.AddSingleton(new MaterialPolling(
            Interval: TimeSpan.FromMilliseconds(10),
            SlowInterval: TimeSpan.FromMilliseconds(10),
            SlowAfter: TimeSpan.FromMilliseconds(20),
            AnalysisLimit: TimeSpan.FromMilliseconds(40)));
    }

    [Fact]
    public void Field_HasAVisibleLabelAndHint()
    {
        var page = RenderPage(new FakeLearningMaterial());

        Assert.Equal(FeedbackTexts.Upload.Label, page.Find("label[for=upload-file]").TextContent);
        var input = page.Find("input#upload-file");
        Assert.Equal("file", input.GetAttribute("type"));
        Assert.Equal("upload-hint", input.GetAttribute("aria-describedby"));
        Assert.Equal(FeedbackTexts.Upload.Hint, page.Find("#upload-hint").TextContent);
    }

    // Since #282 the worker reads the file, so the page follows it like a pasted text until its topics are there.
    [Fact]
    public async Task File_IsUploaded_AndFollowedUntilItsTopicsAreThere()
    {
        var material = new FakeLearningMaterial()
            .Documents()
            .Documents(FileDocument() with { Status = DocumentStatus.Ready })
            .Upload(CallOutcome<DocumentInfo>.Ok(FileDocument()));
        var page = RenderPage(material);

        Choose(page, InputFileContent.CreateFromText("Ein kurzer Text.", "Notizen.txt"));
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        var uploaded = Assert.Single(material.Uploaded);
        Assert.Equal("Notizen.txt", uploaded.FileName);
        Assert.Equal("Ein kurzer Text."u8.ToArray(), uploaded.Content);
        Assert.Empty(Alert(page).Children);
        page.WaitForAssertion(() => Assert.Contains("Die Themen aus „Notizen“ sind da.", Status(page)), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task FileOver20MB_IsRefusedInTheBrowser_WithoutUpload()
    {
        var material = new FakeLearningMaterial();
        var page = RenderPage(material);

        Choose(page, InputFileContent.CreateFromBinary(new byte[(20 * 1024 * 1024) + 1], "Skript.pdf"));
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Empty(material.Uploaded);
        Assert.Contains(FeedbackTexts.Upload.TooLarge, Alert(page).TextContent);
    }

    [Fact]
    public async Task OtherType_IsRefusedInTheBrowser_WithoutUpload()
    {
        var material = new FakeLearningMaterial();
        var page = RenderPage(material);

        Choose(page, InputFileContent.CreateFromBinary([1, 2, 3], "Folien.pptx"));
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Empty(material.Uploaded);
        Assert.Contains(FeedbackTexts.Upload.Unsupported, Alert(page).TextContent);
    }

    // Content's own verdict on the bytes shows as it is.
    [Fact]
    public async Task FileContentRefused_ShowsContentsMessage()
    {
        const string mismatch = "Der Inhalt der Datei passt nicht zu ihrer Endung. Lade die Datei im richtigen Format hoch.";
        var material = new FakeLearningMaterial()
            .Upload(new CallOutcome<DocumentInfo>(CallResult.Rejected, Messages: [mismatch]));
        var page = RenderPage(material);

        Choose(page, InputFileContent.CreateFromBinary([0x89, 0x50, 0x4E, 0x47], "Bild.pdf"));
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Single(material.Uploaded);
        Assert.Contains(mismatch, Alert(page).TextContent);
    }

    [Fact]
    public async Task ServerSaysTooLarge_ShowsTheLimit()
    {
        var material = new FakeLearningMaterial().Upload(new CallOutcome<DocumentInfo>(CallResult.TooLarge, Messages: []));
        var page = RenderPage(material);

        Choose(page, InputFileContent.CreateFromText("Ein kurzer Text.", "Notizen.md"));
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Contains(FeedbackTexts.Upload.TooLarge, Alert(page).TextContent);
    }

    // #307: a limit says why in the urgent live region, as a warning without a trace, and the file stays chosen, so the
    // same button sends it later.
    [Theory]
    [InlineData("Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.", "Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.")]
    [InlineData(null, FeedbackTexts.Limit.Reached)]
    public async Task Limited_SaysWhy_AndKeepsTheFile(string? sentence, string shown)
    {
        var material = new FakeLearningMaterial()
            .Upload(new CallOutcome<DocumentInfo>(CallResult.Limited, Messages: sentence is null ? [] : [sentence]))
            .Upload(new CallOutcome<DocumentInfo>(CallResult.Limited, Messages: sentence is null ? [] : [sentence]));
        var page = RenderPage(material);

        Choose(page, InputFileContent.CreateFromText("Ein kurzer Text.", "Notizen.txt"));
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Equal(shown, Alert(page).QuerySelector(".notice-warning .notice-title")!.TextContent);
        Assert.DoesNotContain(FeedbackTexts.Upload.NotThrough, Alert(page).TextContent);
        Assert.Empty(page.FindAll(".request-id"));

        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Equal(["Notizen.txt", "Notizen.txt"], material.Uploaded.Select(upload => upload.FileName));
    }

    [Fact]
    public async Task NothingChosen_SaysSo()
    {
        var material = new FakeLearningMaterial();
        var page = RenderPage(material);

        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Empty(material.Uploaded);
        Assert.Contains(FeedbackTexts.Upload.NothingChosen, Alert(page).TextContent);
    }

    private static void Choose(IRenderedComponent<Topics> page, InputFileContent file) =>
        page.FindComponent<InputFile>().UploadFiles(file);

    private IRenderedComponent<Topics> RenderPage(FakeLearningMaterial material)
    {
        Services.AddSingleton<ILearningMaterial>(material);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Topics>();
    }

    private static string Status(IRenderedComponent<Topics> page) => page.Find("[role=status]").TextContent;

    // The live region of the upload field.
    private static AngleSharp.Dom.IElement Alert(IRenderedComponent<Topics> page) =>
        page.Find("section[aria-labelledby=upload-heading] [role=alert]");

    private static DocumentInfo FileDocument() =>
        new(Uploaded, "Notizen", DocumentStatus.Uploaded, Created, Source: DocumentSource.File);
}