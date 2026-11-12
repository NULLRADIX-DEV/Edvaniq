using Bunit;

using Edvaniq.Client.DesignSystem;
using Edvaniq.Client.UI.Pages;

namespace Edvaniq.Client.UnitTests;

// The text field from the shared library: a label for the field, required said in words, hint and error tied to the
// field, so a screen reader reads them along.
public sealed class TextFieldTests : BunitContext
{
    [Fact]
    public void Label_BelongsToTheField()
    {
        var field = Render<TextField>(parameters => parameters.Add(p => p.Id, "name").Add(p => p.Label, "Name"));

        Assert.Equal("name", field.Find("label").GetAttribute("for"));
        Assert.Equal("Name", field.Find("label").TextContent);
        var input = field.Find("input#name");
        Assert.False(input.HasAttribute("required"));
        Assert.False(input.HasAttribute("aria-invalid"));
        Assert.False(input.HasAttribute("aria-describedby"));
    }

    [Fact]
    public void Required_IsSaidInWords()
    {
        var field = Render<TextField>(parameters => parameters
            .Add(p => p.Id, "name").Add(p => p.Label, "Name").Add(p => p.Required, true));

        Assert.Equal("Name (Pflichtfeld)", field.Find("label").TextContent);
        Assert.True(field.Find("input").HasAttribute("required"));
    }

    [Fact]
    public void Error_IsTiedToTheField_AndMarksItInvalid()
    {
        var field = Render<TextField>(parameters => parameters
            .Add(p => p.Id, "mail").Add(p => p.Label, "E-Mail")
            .Add(p => p.Hint, "Nur wenn du willst.").Add(p => p.Error, "Gib eine E-Mail-Adresse ein."));

        var input = field.Find("input");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal("mail-hint mail-error", input.GetAttribute("aria-describedby"));
        Assert.Equal("Nur wenn du willst.", field.Find("#mail-hint").TextContent);
        Assert.Equal("Fehler: Gib eine E-Mail-Adresse ein.", field.Find("#mail-error").TextContent.Trim());
        Assert.Equal("true", field.Find("#mail-error svg").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Value_BindsOnChange_AndOtherAttributesReachTheInput()
    {
        var value = "";
        var field = Render<TextField>(parameters => parameters
            .Add(p => p.Id, "title").Add(p => p.Label, "Titel")
            .Add(p => p.ValueChanged, changed => value = changed)
            .AddUnmatched("autocomplete", "off"));

        field.Find("input").Change("Zellen");

        Assert.Equal("Zellen", value);
        Assert.Equal("off", field.Find("input").GetAttribute("autocomplete"));
    }

    [Fact]
    public void Immediate_BindsOnEveryKey()
    {
        var value = "";
        var field = Render<TextField>(parameters => parameters
            .Add(p => p.Id, "title").Add(p => p.Label, "Titel").Add(p => p.Immediate, true)
            .Add(p => p.ValueChanged, changed => value = changed));

        field.Find("input").Input("Z");

        Assert.Equal("Z", value);
    }

    [Fact]
    public void Multiline_IsATextArea_WithTheSameLinks()
    {
        var field = Render<TextField>(parameters => parameters
            .Add(p => p.Id, "text").Add(p => p.Label, "Text").Add(p => p.Multiline, true).Add(p => p.Rows, 4)
            .Add(p => p.Required, true).Add(p => p.Error, "Der Text fehlt."));

        var area = field.Find("textarea#text");
        Assert.Equal("4", area.GetAttribute("rows"));
        Assert.True(area.HasAttribute("required"));
        Assert.Equal("text-error", area.GetAttribute("aria-describedby"));
        Assert.Empty(field.FindAll("input"));
    }
}

// Messages carry their kind in shape and word, never in color alone.
public sealed class NoticeTests : BunitContext
{
    [Theory]
    [InlineData(NoticeKind.Info, "Hinweis")]
    [InlineData(NoticeKind.Success, "Erfolg")]
    [InlineData(NoticeKind.Warning, "Warnung")]
    [InlineData(NoticeKind.Error, "Fehler")]
    public void Kind_IsSaidInWords_AndShownAsASymbol(NoticeKind kind, string word)
    {
        var notice = Render<Notice>(parameters => parameters
            .Add(p => p.Kind, kind).Add(p => p.Title, "Titel")
            .AddChildContent("<p>Mehr dazu.</p>"));

        var root = notice.Find(".notice");
        Assert.Contains($"notice-{kind.ToString().ToLowerInvariant()}", root.ClassList);
        Assert.Equal($"{word}: ", root.QuerySelector(".visually-hidden")!.TextContent);
        Assert.Equal("true", root.QuerySelector("svg")!.GetAttribute("aria-hidden"));
        Assert.Equal("Titel", notice.Find(".notice-title").TextContent);
        Assert.Equal("Mehr dazu.", notice.Find(".notice-body > p:last-child").TextContent);
    }

    [Fact]
    public void EveryKind_HasItsOwnShape()
    {
        var shapes = Enum.GetValues<NoticeKind>()
            .Select(kind => Render<StatusSymbol>(parameters => parameters.Add(p => p.Kind, kind)).Find("svg").InnerHtml)
            .ToList();

        Assert.Equal(shapes.Count, shapes.Distinct().Count());
    }

    [Fact]
    public void WithoutTitle_ShowsOnlyTheContent()
    {
        var notice = Render<Notice>(parameters => parameters.AddChildContent("<p>Nur Inhalt.</p>"));

        Assert.Empty(notice.FindAll(".notice-title"));
        Assert.Contains("notice-info", notice.Find(".notice").ClassList);
    }

    // #273: a few answers as radio buttons. The legend asks, hint and error hang on the group, the error marks the answers.
    [Fact]
    public void ChoiceField_IsAGroupWithHintAndError()
    {
        string? chosen = null;
        var field = Render<ChoiceField>(parameters => parameters
            .Add(p => p.Id, "kind").Add(p => p.Label, "Wie lernst du?").Add(p => p.Required, true)
            .Add(p => p.Options, [new ChoiceOption("a", "Erstens"), new ChoiceOption("b", "Zweitens")])
            .Add(p => p.Value, "b").Add(p => p.Hint, "Such dir eins aus.").Add(p => p.Error, "Wähl eins.")
            .Add(p => p.ValueChanged, value => chosen = value));

        Assert.Equal("Wie lernst du? (Pflichtfeld)", field.Find("fieldset > legend").TextContent);
        Assert.Equal("kind-hint kind-error", field.Find("fieldset").GetAttribute("aria-describedby"));
        Assert.Equal(["kind-a", "kind-b"], field.FindAll("input[type=radio][name=kind]").Select(input => input.Id));
        Assert.Equal("Erstens", field.Find("label[for=kind-a]").TextContent);
        Assert.True(field.Find("#kind-b").HasAttribute("checked"));
        Assert.All(field.FindAll("input"), input => Assert.Equal("true", input.GetAttribute("aria-invalid")));

        field.Find("#kind-a").Change("a");

        Assert.Equal("a", chosen);
    }

    [Fact]
    public void SelectField_SaysWhatTheEmptyChoiceMeans()
    {
        string? chosen = "x";
        var required = Render<SelectField>(parameters => parameters
            .Add(p => p.Id, "type").Add(p => p.Label, "Art").Add(p => p.Required, true)
            .Add(p => p.Options, [new ChoiceOption("ex", "Ex")]).Add(p => p.Error, "Wähl eine Art.")
            .Add(p => p.ValueChanged, value => chosen = value));
        var optional = Render<SelectField>(parameters => parameters
            .Add(p => p.Id, "level").Add(p => p.Label, "Stufe").Add(p => p.Options, [new ChoiceOption("u", "Unterstufe")]));

        Assert.Equal(["Bitte wählen", "Ex"], required.FindAll("option").Select(option => option.TextContent));
        Assert.Equal("true", required.Find("select").GetAttribute("aria-invalid"));
        Assert.Equal("type-error", required.Find("select").GetAttribute("aria-describedby"));
        Assert.Equal("Keine Angabe", optional.FindAll("option")[0].TextContent);

        required.Find("select").Change("");

        Assert.Null(chosen);
    }

    // A class from the caller marks the place, the notice keeps its own look.
    [Fact]
    public void ExtraClass_AddsToTheNoticesOwn()
    {
        var notice = Render<Notice>(parameters => parameters
            .Add(p => p.Kind, NoticeKind.Error)
            .AddUnmatched("class", "connection-problem")
            .AddUnmatched("id", "problem"));

        var root = notice.Find("#problem");
        Assert.Equal(["notice", "notice-error", "connection-problem"], root.ClassList);
    }
}

// A status region is there before its content, so a screen reader announces what appears in it.
public sealed class LiveRegionTests : BunitContext
{
    [Fact]
    public void IsAStatus_AndThereWhileEmpty()
    {
        var region = Render<LiveRegion>();

        Assert.Equal("status", region.Find("div").GetAttribute("role"));
        Assert.Equal("", region.Find("div").TextContent.Trim());
    }

    [Fact]
    public void Urgent_IsAnAlert()
    {
        var region = Render<LiveRegion>(parameters => parameters.Add(p => p.Urgent, true).AddChildContent("<p>Weg.</p>"));

        Assert.Equal("alert", region.Find("div").GetAttribute("role"));
        Assert.Equal("Weg.", region.Find("[role=alert] p").TextContent);
    }

    [Fact]
    public void Loading_ReadsOnlyTheText()
    {
        var loading = Render<Loading>();

        Assert.Equal("Wird geladen …", loading.Find(".loading").TextContent);
        Assert.Equal("true", loading.Find(".loading-spinner").GetAttribute("aria-hidden"));
    }
}

// The sample page shows every building block in every state, and its small form uses them like a real page would.
public sealed class BuildingBlocksPageTests : BunitContext
{
    [Fact]
    public void ShowsEveryBlockInEveryState()
    {
        var page = Render<BuildingBlocks>();

        Assert.Equal("Bausteine", page.Find("h1").TextContent);
        Assert.NotEmpty(page.FindAll("label .field-required"));
        Assert.NotEmpty(page.FindAll(".field-hint"));
        Assert.NotEmpty(page.FindAll("[aria-invalid=true]"));
        Assert.NotEmpty(page.FindAll("textarea"));
        foreach (var kind in Enum.GetValues<NoticeKind>())
        {
            Assert.NotEmpty(page.FindAll($".notice-{kind.ToString().ToLowerInvariant()}"));
        }

        Assert.NotEmpty(page.FindAll("[role=status] .loading"));
    }

    [Fact]
    public void Form_ShowsTheErrorAtTheField_ThenTheResultInTheStatus()
    {
        var page = Render<BuildingBlocks>();

        page.Find(".block-form").Submit();
        Assert.Equal("true", page.Find("#sample-check").GetAttribute("aria-invalid"));
        Assert.Equal("Fehler: Gib ein Thema ein.", page.Find("#sample-check-error").TextContent.Trim());

        page.Find("#sample-check").Change("Photosynthese");
        page.Find(".block-form").Submit();
        Assert.False(page.Find("#sample-check").HasAttribute("aria-invalid"));
        Assert.Contains("„Photosynthese“ ist eingetragen.", page.Find(".block-form [role=status]").TextContent);
    }
}