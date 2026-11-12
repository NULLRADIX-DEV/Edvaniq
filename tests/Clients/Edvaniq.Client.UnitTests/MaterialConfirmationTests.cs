using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #278: one checkbox above both forms, nothing goes up without it. Without the tick the page says so at the field and
// sends nothing, Content's own word on it lands there too.
public sealed class MaterialConfirmationTests : BunitContext
{
    private static readonly DocumentInfo Pasted = new(Guid.NewGuid(), "Skript", DocumentStatus.Ready, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Checkbox_SaysWhatIsConfirmed_AndStartsUnticked()
    {
        var page = RenderPage(new FakeLearningMaterial());

        var box = page.Find("input#material-confirm");
        Assert.Equal("checkbox", box.GetAttribute("type"));
        Assert.False(box.HasAttribute("checked"));
        Assert.Equal("material-confirm-message", box.GetAttribute("aria-describedby"));
        Assert.Null(box.GetAttribute("aria-invalid"));
        Assert.Equal(MaterialConfirmationValues.Text, page.Find("label[for=material-confirm]").TextContent);
    }

    [Fact]
    public void Wording_NamesOwnMaterial_AndExcludesOthersData()
    {
        Assert.Contains("eigenes Material", MaterialConfirmationValues.Text);
        Assert.Contains("keine Daten anderer Personen", MaterialConfirmationValues.Text);
    }

    [Fact]
    public void PasteWithoutTick_IsNotSent_AndSaysSoAtTheField()
    {
        var material = new FakeLearningMaterial().Paste(CallOutcome<DocumentInfo>.Ok(Pasted));
        var page = RenderPage(material);

        page.Find("#paste-text").Change("Mein Text");
        page.Find("form").Submit();

        Assert.Empty(material.Pasted);
        Assert.Equal(MaterialConfirmationValues.Missing, Message(page));
        Assert.Equal("true", page.Find("#material-confirm").GetAttribute("aria-invalid"));
        Assert.Equal("Mein Text", page.Find("#paste-text").GetAttribute("value"));
        Assert.Empty(page.FindAll(".material-form-message"));
    }

    [Fact]
    public async Task UploadWithoutTick_IsNotSent_AndSaysSoAtTheField()
    {
        var material = new FakeLearningMaterial().Upload(CallOutcome<DocumentInfo>.Ok(Pasted));
        var page = RenderPage(material);

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("Ein kurzer Text.", "Notizen.txt"));
        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Empty(material.Uploaded);
        Assert.Equal(MaterialConfirmationValues.Missing, Message(page));
        Assert.Empty(page.FindAll(".material-upload-message"));
    }

    [Fact]
    public void Tick_TakesTheMessageAway_AndTheTextGoesUp()
    {
        var material = new FakeLearningMaterial().Paste(CallOutcome<DocumentInfo>.Ok(Pasted));
        var page = RenderPage(material);
        page.Find("#paste-text").Change("Mein Text");
        page.Find("form").Submit();

        page.Confirm();

        Assert.Empty(page.Find("#material-confirm-message").TextContent.Trim());
        Assert.Null(page.Find("#material-confirm").GetAttribute("aria-invalid"));
        page.Find("form").Submit();
        Assert.Equal([(null, "Mein Text")], material.Pasted);
    }

    // The user confirmed for this visit, a second text needs no second tick.
    [Fact]
    public void Tick_StaysAfterASend()
    {
        var material = new FakeLearningMaterial().Paste(CallOutcome<DocumentInfo>.Ok(Pasted));
        var page = RenderPage(material);
        page.Confirm();
        page.Find("#paste-text").Change("Erster Text");
        page.Find("form").Submit();

        page.Find("#paste-text").Change("Zweiter Text");
        page.Find("form").Submit();

        Assert.Equal(["Erster Text", "Zweiter Text"], material.Pasted.Select(paste => paste.Text));
        Assert.True(page.Find("#material-confirm").HasAttribute("checked"));
    }

    // Content refuses a version it doesn't know (any more): its message shows at the field, others stay at the form.
    [Fact]
    public void ContentRefusesThePaste_ItsConfirmationMessageGoesToTheField()
    {
        var material = new FakeLearningMaterial().Paste(new CallOutcome<DocumentInfo>(
            CallResult.Rejected, Messages: [MaterialConfirmationValues.Missing, HttpLearningMaterial.TitleTooLong]));
        var page = RenderPage(material);
        page.Confirm();
        page.Find("#paste-text").Change("Mein Text");

        page.Find("form").Submit();

        Assert.Equal(MaterialConfirmationValues.Missing, Message(page));
        var form = page.Find(".material-form-message").TextContent;
        Assert.Contains(HttpLearningMaterial.TitleTooLong, form);
        Assert.DoesNotContain(MaterialConfirmationValues.Missing, form);
    }

    [Fact]
    public async Task ContentRefusesTheUpload_ItsMessageGoesToTheField()
    {
        var material = new FakeLearningMaterial().Upload(new CallOutcome<DocumentInfo>(
            CallResult.Rejected, Messages: [MaterialConfirmationValues.Missing]));
        var page = RenderPage(material);
        page.Confirm();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("Ein kurzer Text.", "Notizen.txt"));

        await page.Find("#upload-heading ~ button").ClickAsync(new());

        Assert.Single(material.Uploaded);
        Assert.Equal(MaterialConfirmationValues.Missing, Message(page));
        Assert.Empty(page.FindAll(".material-upload-message"));
    }

    private IRenderedComponent<Topics> RenderPage(FakeLearningMaterial material)
    {
        Services.AddSingleton<ILearningMaterial>(material);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Topics>();
    }

    private static string Message(IRenderedComponent<Topics> page) =>
        page.Find("#material-confirm-message[role=alert] .confirm-message").TextContent;
}