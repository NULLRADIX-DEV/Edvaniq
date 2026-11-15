using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #295: renaming, hiding with undo and adding topics and subtopics in the directory, and how each refusal is said.
public sealed class TopicEditingTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly Guid Goal = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid OtherGoal = Guid.Parse("2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e");
    private static readonly Guid Photosynthese = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Lichtreaktion = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    private static readonly Guid Dunkelreaktion = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");
    private static readonly Guid Zellatmung = Guid.Parse("8c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f");

    private readonly FakeTopicDirectory directory = new()
    {
        Topics = List(
            new(Photosynthese, "Photosynthese", [new(Lichtreaktion, "Lichtreaktion"), new(Dunkelreaktion, "Dunkelreaktion", Hidden: true)]),
            new(Zellatmung, "Zellatmung", [])),
    };

    private readonly FakeTopicEntries entries = new();
    private FakeLearningGoals goals = new FakeLearningGoals().List(GoalInfo(Goal, "Biologie"));

    [Fact]
    public void Rename_SendsTheNewName_AndTheDirectorySaysSo()
    {
        entries.OnChange = (_, _) => directory.Topics = List(new(Photosynthese, "Fotosynthese", []), new(Zellatmung, "Zellatmung", []));
        var page = RenderPage();

        Open(page, "Photosynthese");
        Tool(page, "Photosynthese", FeedbackTexts.TopicEditing.Rename).Click();
        var field = page.Find($"#rename-{Photosynthese:N}");
        Assert.Equal("Photosynthese", field.GetAttribute("value"));
        field.Input(" Fotosynthese ");
        page.Find(".topic-form").Submit();

        Assert.Equal([$"Rename {Photosynthese} Fotosynthese"], entries.Changes);
        Assert.Contains(string.Format(FeedbackTexts.TopicEditing.Renamed, "Fotosynthese"), EditStatus(page));
        Assert.NotNull(Toggle(page, "Fotosynthese"));
        Assert.Empty(page.FindAll(".topic-form"));
    }

    // Same name as before: nothing to send, the form just closes. Another spelling is a change.
    [Fact]
    public void Rename_ToTheSameName_SendsNothing_ButAnotherSpellingIsSent()
    {
        var page = RenderPage();
        Open(page, "Zellatmung");

        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Rename).Click();
        page.Find($"#rename-{Zellatmung:N}").Input("Zellatmung ");
        page.Find(".topic-form").Submit();

        Assert.Empty(entries.Changes);
        Assert.Empty(page.FindAll(".topic-form"));

        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Rename).Click();
        page.Find($"#rename-{Zellatmung:N}").Input("zellatmung");
        page.Find(".topic-form").Submit();

        Assert.Equal([$"Rename {Zellatmung} zellatmung"], entries.Changes);
    }

    [Fact]
    public void Escape_ClosesTheForm_WithoutSending()
    {
        var page = RenderPage();
        Open(page, "Zellatmung");
        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Rename).Click();
        page.Find($"#rename-{Zellatmung:N}").Input("Atmung");

        page.Find(".topic-form").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(page.FindAll(".topic-form"));
        Assert.Empty(entries.Changes);
        Assert.NotNull(Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Rename));
    }

    // Empty and too long are said before anything is sent, with Knowledge's words.
    [Theory]
    [InlineData("  ", FeedbackTexts.TopicEditing.NameEmpty)]
    [InlineData("x", FeedbackTexts.Goal.TopicTooLong)]
    public void Rename_WithANameKnowledgeWouldRefuse_SaysSoAtTheField(string name, string message)
    {
        var page = RenderPage();
        Open(page, "Zellatmung");
        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Rename).Click();

        page.Find($"#rename-{Zellatmung:N}").Input(name == "x" ? new string('x', TopicValues.NameMaxLength + 1) : name);
        page.Find(".topic-form").Submit();

        AssertFieldError(page, $"rename-{Zellatmung:N}", message);
        Assert.Empty(entries.Changes);
    }

    [Fact]
    public void Rename_RefusedByKnowledge_SaysWhyAtTheField()
    {
        entries
            .Edit(new CallOutcome<TopicEdit>(CallResult.Rejected, new TopicEdit(), FieldErrors: new Dictionary<string, string> { ["Name"] = "Satz von Knowledge." }))
            .Edit(new CallOutcome<TopicEdit>(CallResult.Rejected, new TopicEdit(Conflict: TopicConflict.NameTaken)));
        var page = RenderPage();
        Open(page, "Zellatmung");
        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Rename).Click();

        page.Find($"#rename-{Zellatmung:N}").Input("Photosynthese");
        page.Find(".topic-form").Submit();
        AssertFieldError(page, $"rename-{Zellatmung:N}", "Satz von Knowledge.");

        page.Find(".topic-form").Submit();
        AssertFieldError(page, $"rename-{Zellatmung:N}", FeedbackTexts.TopicEditing.NameTaken);
    }

    // A topic gone meanwhile says so and offers to load anew.
    [Fact]
    public void Rename_OfATopicThatIsGone_SaysSo_AndLoadsAnew()
    {
        entries.Edit(new CallOutcome<TopicEdit>(CallResult.NotFound));
        var page = RenderPage();
        Open(page, "Zellatmung");
        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Rename).Click();
        page.Find($"#rename-{Zellatmung:N}").Input("Atmung");
        page.Find(".topic-form").Submit();

        var panel = Panel(page, "Zellatmung");
        Assert.Contains(FeedbackTexts.TopicEditing.Gone, panel.QuerySelector(".topic-tools-status")!.TextContent);
        var calls = directory.TopicCalls;

        panel.QuerySelectorAll("button").Single(button => button.TextContent == FeedbackTexts.TopicEditing.Reload).Click();

        Assert.Equal(calls + 1, directory.TopicCalls);
    }

    // AC1: hiding says what went along and offers the undo right away, with the focus on it. The undo brings the topic back
    // and its subtopics in their own state: one hidden before stays hidden.
    [Fact]
    public void Hide_TakesTheSubtopicsAlong_AndUndo_BringsThemBackAsTheyWere()
    {
        var before = directory.Topics;
        entries.OnChange = (kind, _) => directory.Topics = kind == "Hide"
            ? List(
                new(Photosynthese, "Photosynthese", [new(Lichtreaktion, "Lichtreaktion", Hidden: true), new(Dunkelreaktion, "Dunkelreaktion", Hidden: true)], Hidden: true),
                new(Zellatmung, "Zellatmung", []))
            : before;
        var page = RenderPage();
        Open(page, "Photosynthese");

        Tool(page, "Photosynthese", FeedbackTexts.TopicEditing.Hide).Click();

        Assert.Equal([$"Hide {Photosynthese}"], entries.Changes);
        Assert.Contains(string.Format(FeedbackTexts.TopicEditing.HiddenWithOneSubtopic, "Photosynthese"), EditStatus(page));
        Assert.Contains(FeedbackTexts.TopicEditing.HiddenWhere, EditStatus(page));
        Assert.Equal(["Zellatmung"], VisibleTopics(page));
        var undo = page.Find(".directory-edit-actions button");
        Assert.Equal(FeedbackTexts.TopicEditing.Undo, undo.TextContent);
        Assert.Contains(JSInterop.Invocations, call => call.Identifier == "Blazor._internal.domWrapper.focus");

        undo.Click();

        Assert.Equal([$"Hide {Photosynthese}", $"Show {Photosynthese}"], entries.Changes);
        Assert.Contains(string.Format(FeedbackTexts.TopicEditing.Shown, "Photosynthese"), EditStatus(page));
        Assert.Equal(["Photosynthese", "Zellatmung"], VisibleTopics(page));
        Assert.Empty(page.FindAll(".directory-edit-actions"));
        Open(page, "Photosynthese");
        Assert.Contains("Lichtreaktion", Panel(page, "Photosynthese").TextContent);
        Assert.Equal(["Dunkelreaktion"], page.FindAll("#hidden-topics .topic-entry > h3").Select(h => h.TextContent.Trim()));
    }

    [Theory]
    [InlineData(0, FeedbackTexts.TopicEditing.HiddenOne)]
    [InlineData(3, FeedbackTexts.TopicEditing.HiddenWithSubtopics)]
    public void HiddenMessage_CountsTheSubtopics(int subtopics, string text)
    {
        Assert.Equal(string.Format(text, "Zelle", subtopics), TopicEditTexts.Hidden("Zelle", subtopics));
    }

    [Fact]
    public void HiddenTopic_ComesBackFromTheHiddenOnes()
    {
        var page = RenderPage();
        page.Find(".directory-hidden h2 button").Click();
        Open(page, "Dunkelreaktion");

        Tool(page, "Dunkelreaktion", FeedbackTexts.TopicEditing.Show).Click();

        Assert.Equal([$"Show {Dunkelreaktion}"], entries.Changes);
        Assert.Contains(string.Format(FeedbackTexts.TopicEditing.Shown, "Dunkelreaktion"), EditStatus(page));
    }

    // A subtopic of a hidden topic comes back only with it, so it offers no way back of its own.
    [Fact]
    public void SubtopicOfAHiddenTopic_CannotComeBackAlone()
    {
        directory.Topics = List(new TopicInfo(Photosynthese, "Photosynthese", [new(Lichtreaktion, "Lichtreaktion", Hidden: true)], Hidden: true));
        var page = RenderPage();
        page.Find(".directory-hidden h2 button").Click();
        Open(page, "Photosynthese");
        Open(page, "Lichtreaktion");

        var tools = Panel(page, "Lichtreaktion").QuerySelectorAll(".topic-tools button").Select(button => button.TextContent);

        Assert.Equal([FeedbackTexts.TopicEditing.Rename], tools);
    }

    // AC2: each refusal of a hide says what it means; what can be tried again offers that, a gone topic loading anew.
    [Fact]
    public void Hide_Refused_SaysWhy_AndOffersTheWayOn()
    {
        entries
            .Edit(new CallOutcome<TopicEdit>(CallResult.Rejected, new TopicEdit(Conflict: TopicConflict.Busy)))
            .Edit(CallOutcome<TopicEdit>.Failed(TraceId))
            .Edit(new CallOutcome<TopicEdit>(CallResult.NotFound));
        var page = RenderPage();
        Open(page, "Zellatmung");

        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.Hide).Click();
        Assert.Contains(FeedbackTexts.TopicEditing.Busy, EditStatus(page));
        Assert.Equal(FeedbackTexts.TopicEditing.TryAgain, page.Find(".directory-edit-actions button").TextContent);

        page.Find(".directory-edit-actions button").Click();
        Assert.Contains(FeedbackTexts.TopicEditing.NotThrough, EditStatus(page));
        Assert.Contains(TraceId, EditStatus(page));

        page.Find(".directory-edit-actions button").Click();
        Assert.Contains(FeedbackTexts.TopicEditing.Gone, EditStatus(page));
        Assert.Equal(FeedbackTexts.TopicEditing.Reload, page.Find(".directory-edit-actions button").TextContent);
        Assert.Equal(3, entries.Changes.Count);
    }

    // Showing a subtopic whose topic is hidden meanwhile: trying again would not help.
    [Fact]
    public void Show_UnderAHiddenTopic_SaysSo_WithoutTryingAgain()
    {
        entries.Edit(new CallOutcome<TopicEdit>(CallResult.Rejected, new TopicEdit(Conflict: TopicConflict.ParentHidden)));
        var page = RenderPage();
        page.Find(".directory-hidden h2 button").Click();
        Open(page, "Dunkelreaktion");

        Tool(page, "Dunkelreaktion", FeedbackTexts.TopicEditing.Show).Click();

        Assert.Contains(FeedbackTexts.TopicEditing.ParentHidden, EditStatus(page));
        Assert.Empty(page.FindAll(".directory-edit-actions"));
    }

    // AC1: a topic in the only goal, without asking for it.
    [Fact]
    public void AddTopic_GoesToTheGoal_AndIsSaid()
    {
        var page = RenderPage();
        Assert.Empty(page.FindAll("#topic-add-goal"));

        page.Find("#topic-add-name").Input("Zellteilung");
        page.Find(".directory-add-form").Submit();

        Assert.Equal(new[] { $"CreateTopic {Goal} Zellteilung" }, entries.Changes);
        Assert.Contains(string.Format(FeedbackTexts.TopicEditing.Added, "Zellteilung"), EditStatus(page));
        Assert.Equal("", page.Find("#topic-add-name").GetAttribute("value"));
    }

    [Fact]
    public void AddTopic_WithSeveralGoals_AsksWhichOne()
    {
        goals = new FakeLearningGoals().List(GoalInfo(Goal, "Biologie"), GoalInfo(OtherGoal, "Chemie", inPlan: true));
        var page = RenderPage();

        var select = page.Find("#topic-add-goal");
        Assert.Equal(["Biologie", "Chemie"], select.QuerySelectorAll("option[value]:not([value=''])").Select(option => option.TextContent));
        select.Change(Goal.ToString());
        page.Find("#topic-add-name").Input("Zellteilung");
        page.Find(".directory-add-form").Submit();

        Assert.Equal(new[] { $"CreateTopic {Goal} Zellteilung" }, entries.Changes);
    }

    [Fact]
    public void AddTopic_WithATakenName_SaysSoAtTheField()
    {
        entries.Edit(new CallOutcome<TopicEdit>(CallResult.Rejected, new TopicEdit(Conflict: TopicConflict.NameTaken)));
        var page = RenderPage();

        page.Find("#topic-add-name").Input("Zellatmung");
        page.Find(".directory-add-form").Submit();

        AssertFieldError(page, "topic-add-name", FeedbackTexts.TopicEditing.NameTaken);
        Assert.Equal("Zellatmung", page.Find("#topic-add-name").GetAttribute("value"));
    }

    [Fact]
    public void WithoutAGoal_AddingLeadsToCreatingOne()
    {
        goals = new FakeLearningGoals();
        var page = RenderPage();

        Assert.Empty(page.FindAll(".directory-add-form"));
        Assert.Equal("lernziele/neu", page.Find(".directory-add a").GetAttribute("href"));
    }

    [Fact]
    public void AddSubtopic_GoesUnderTheTopic()
    {
        var page = RenderPage();
        Open(page, "Zellatmung");

        Tool(page, "Zellatmung", FeedbackTexts.TopicEditing.AddSubtopic).Click();
        page.Find($"#subtopic-{Zellatmung:N}").Input("Glykolyse");
        page.Find(".topic-form").Submit();

        Assert.Equal([$"CreateSubtopic {Zellatmung} Glykolyse"], entries.Changes);
        Assert.Contains(string.Format(FeedbackTexts.TopicEditing.Added, "Glykolyse"), EditStatus(page));
    }

    // A subtopic takes no subtopics, a topic has two levels.
    [Fact]
    public void Subtopic_OffersNoSubtopics()
    {
        var page = RenderPage();
        Open(page, "Photosynthese");
        Open(page, "Lichtreaktion");

        var tools = Panel(page, "Lichtreaktion").QuerySelectorAll(":scope > .topic-tools button").Select(button => button.TextContent);

        Assert.Equal([FeedbackTexts.TopicEditing.Rename, FeedbackTexts.TopicEditing.Hide], tools);
    }

    private static CallOutcome<IReadOnlyList<TopicInfo>> List(params TopicInfo[] topics) => CallOutcome<IReadOnlyList<TopicInfo>>.Ok(topics);

    private static LearningGoalInfo GoalInfo(Guid id, string name, bool inPlan = false) =>
        new(id, LearningGoalValues.FreeLearning, name, null, null, null, "de", null, DateTime.UnixEpoch, inPlan);

    private IRenderedComponent<TopicDirectory> RenderPage()
    {
        Services.AddSingleton<ITopicDirectory>(directory);
        Services.AddSingleton<ITopicEntries>(entries);
        Services.AddSingleton<ILearningGoals>(goals);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<TopicDirectory>();
    }

    private static void AssertFieldError(IRenderedComponent<TopicDirectory> page, string fieldId, string message)
    {
        var field = page.Find($"#{fieldId}");
        Assert.Equal("true", field.GetAttribute("aria-invalid"));
        Assert.Contains($"{fieldId}-error", field.GetAttribute("aria-describedby")!.Split(' '));
        Assert.EndsWith(message, page.Find($"#{fieldId}-error").TextContent.Trim(), StringComparison.Ordinal);
    }

    private static void Open(IRenderedComponent<TopicDirectory> page, string name)
    {
        if (Toggle(page, name).GetAttribute("aria-expanded") == "false")
        {
            Toggle(page, name).Click();
        }
    }

    private static string EditStatus(IRenderedComponent<TopicDirectory> page) => page.Find(".directory-edit-status").TextContent;

    private static IEnumerable<string> VisibleTopics(IRenderedComponent<TopicDirectory> page) =>
        page.FindAll(".directory-list > li > .topic-entry > h2").Select(h => h.TextContent.Trim());

    private static AngleSharp.Dom.IElement Tool(IRenderedComponent<TopicDirectory> page, string name, string text) =>
        Panel(page, name).QuerySelector(":scope > .topic-tools")!.QuerySelectorAll("button").Single(button => button.TextContent == text);

    private static AngleSharp.Dom.IElement Toggle(IRenderedComponent<TopicDirectory> page, string name) =>
        page.FindAll("button.disclosure").Single(button => button.TextContent.Trim() == name);

    private static AngleSharp.Dom.IElement Panel(IRenderedComponent<TopicDirectory> page, string name) =>
        page.Find($"#{Toggle(page, name).GetAttribute("aria-controls")}");
}