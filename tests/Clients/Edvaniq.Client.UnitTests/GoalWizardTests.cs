using AngleSharp.Dom;

using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #273: the assistant leads through kind, the goal itself, level and language to a summary and saves the goal in
// Planning. Fields hang their errors on themselves, back and on keep everything.
public sealed class GoalWizardTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private readonly FakeLearningGoals goals = new();
    private readonly Drafts drafts = new();
    private readonly FakeTopicEntries topics = new();

    public GoalWizardTests()
    {
        drafts.Claim("ben");
        Services.AddSingleton<ILearningGoals>(goals);
        Services.AddSingleton<ITopicEntries>(topics);
        Services.AddSingleton(drafts);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    // AC1: the kind decides whether exam type and date are asked for.
    [Fact]
    public void Kind_ShowsOrHidesTheExamFields()
    {
        var page = Render<GoalWizard>();
        Choose(page, LearningGoalValues.ExamPreparation);
        Next(page);
        Assert.NotNull(page.Find("#goal-exam-type"));
        Assert.NotNull(page.Find("#goal-exam-date"));
        page.Find("#goal-exam-type").Change("Klausur");

        Back(page);
        Choose(page, LearningGoalValues.FreeLearning);
        Next(page);

        Assert.Empty(page.FindAll("#goal-exam-type"));
        Assert.Empty(page.FindAll("#goal-exam-date"));
        Assert.NotNull(page.Find("#goal-name"));

        // Back to an exam, the type chosen before is still there.
        Back(page);
        Choose(page, LearningGoalValues.ExamPreparation);
        Next(page);
        Assert.Equal("Klausur", page.Find("#goal-exam-type").GetAttribute("value"));
    }

    // AC1 and AC2: every field reaches Planning, exam fields only with an exam.
    [Fact]
    public void Exam_SendsEveryField()
    {
        var page = Render<GoalWizard>();
        FillExam(page);
        Next(page);
        page.Find("#goal-level").Change("UpperSecondary");
        page.Find("#goal-language-en").Change("en");
        Next(page);
        Next(page);

        Assert.Contains("Biologie-Klausur", page.Find(".goal-summary").TextContent);
        Assert.Contains("1. März 2027", page.Find(".goal-summary").TextContent);
        Next(page);

        Assert.Equal(
            new NewLearningGoal("ExamPreparation", "Biologie-Klausur", "Klausur", "2027-03-01", "UpperSecondary", "en", "eine 2"),
            Assert.Single(goals.Created));
    }

    [Fact]
    public void FreeLearning_SendsNoExamFields_AndGermanByDefault()
    {
        var page = Render<GoalWizard>();
        Choose(page, LearningGoalValues.ExamPreparation);
        Next(page);
        page.Find("#goal-exam-type").Change("Ex");
        page.Find("#goal-exam-date").Change("2027-03-01");
        Back(page);
        Choose(page, LearningGoalValues.FreeLearning);
        Next(page);
        page.Find("#goal-name").Change("  C# lernen ");
        Next(page);
        Next(page);
        Next(page);
        Next(page);

        Assert.Equal(new NewLearningGoal("FreeLearning", "C# lernen", null, null, null, "de", null), Assert.Single(goals.Created));
    }

    // AC2: what is missing is said at its field, the step stays, and the focus goes to the first such field.
    [Fact]
    public void MissingKind_IsSaidAtTheField()
    {
        var page = Render<GoalWizard>();
        var first = ReferenceId(page.Find("#goal-kind-ExamPreparation"));

        Next(page);

        Assert.Equal(FeedbackTexts.Goal.KindMissing, ErrorAt(page, "#goal-kind-error"));
        Assert.Contains("goal-kind-error", page.Find("fieldset").GetAttribute("aria-describedby"));
        Assert.Equal("true", page.Find("#goal-kind-ExamPreparation").GetAttribute("aria-invalid"));
        Assert.Contains("Schritt 1 von 5", page.Find("h2").TextContent);
        AssertFocused(first);
    }

    [Fact]
    public void MissingNameAndExamType_AreSaidAtTheirFields()
    {
        var page = Render<GoalWizard>();
        Choose(page, LearningGoalValues.ExamPreparation);
        Next(page);
        var nameField = ReferenceId(page.Find("#goal-name"));
        page.Find("#goal-name").Change(new string('a', LearningGoalValues.NameMaxLength + 1));
        page.Find("#goal-goal").Change(new string('b', LearningGoalValues.GoalMaxLength + 1));

        Next(page);

        Assert.Equal(FeedbackTexts.Goal.NameTooLong, ErrorAt(page, "#goal-name-error"));
        Assert.Equal(FeedbackTexts.Goal.ExamTypeMissing, ErrorAt(page, "#goal-exam-type-error"));
        Assert.Equal(FeedbackTexts.Goal.GoalTooLong, ErrorAt(page, "#goal-goal-error"));
        Assert.Contains("Schritt 2 von 5", page.Find("h2").TextContent);
        AssertFocused(nameField);

        page.Find("#goal-name").Change(" ");
        Next(page);
        Assert.Equal(FeedbackTexts.Goal.NameMissing, ErrorAt(page, "#goal-name-error"));
        Assert.Empty(goals.Created);
    }

    // AC2: what only Planning can judge, a date in the past, comes back to its field in its step.
    [Fact]
    public void PlanningsErrors_LeadToTheirStepAndField()
    {
        goals.Create(new CallOutcome<LearningGoalInfo>(
            CallResult.Rejected,
            FieldErrors: new Dictionary<string, string> { [LearningGoalValues.Fields.ExamDate] = "Der Prüfungstermin darf nicht in der Vergangenheit liegen." }));
        var page = Render<GoalWizard>();
        FillExam(page);
        Next(page);
        Next(page);
        Next(page);
        Next(page);

        Assert.Contains("Schritt 2 von 5", page.Find("h2").TextContent);
        Assert.Equal("Der Prüfungstermin darf nicht in der Vergangenheit liegen.", ErrorAt(page, "#goal-exam-date-error"));
        Assert.Equal("true", page.Find("#goal-exam-date").GetAttribute("aria-invalid"));
        Assert.Equal("Biologie-Klausur", page.Find("#goal-name").GetAttribute("value"));
        AssertFocused(page, "#goal-exam-date");
    }

    // AC3: back and on again, everything entered is still there.
    [Fact]
    public void BackAndOn_KeepEverything()
    {
        var page = Render<GoalWizard>();
        FillExam(page);
        Next(page);
        page.Find("#goal-level").Change("Apprenticeship");
        Next(page);

        Back(page);
        Assert.Equal("Apprenticeship", page.Find("#goal-level").GetAttribute("value"));
        Back(page);
        Assert.Equal("Biologie-Klausur", page.Find("#goal-name").GetAttribute("value"));
        Assert.Equal("Klausur", page.Find("#goal-exam-type").GetAttribute("value"));
        Assert.Equal("2027-03-01", page.Find("#goal-exam-date").GetAttribute("value"));
        Assert.Equal("eine 2", page.Find("#goal-goal").GetAttribute("value"));
        Back(page);
        Assert.True(page.Find("#goal-kind-ExamPreparation").HasAttribute("checked"));
        Assert.Empty(page.FindAll(".goal-back"));
    }

    // AC4: each step change puts the focus on the step's heading, which says where the user is.
    [Fact]
    public void StepChange_FocusesTheHeading()
    {
        var page = Render<GoalWizard>();
        Choose(page, LearningGoalValues.FreeLearning);

        Next(page);

        Assert.Equal("-1", page.Find("h2").GetAttribute("tabindex"));
        Assert.Contains($"Schritt 2 von 5: {FeedbackTexts.Goal.AboutStep}", Squeeze(page.Find("h2").TextContent));
        AssertFocused(page, "h2");
        Assert.Equal("submit", page.Find(".goal-actions .button").GetAttribute("type"));
        Assert.Equal("button", page.Find(".goal-actions .goal-back").GetAttribute("type"));
    }

    [Fact]
    public void FirstGoal_IsSaved_AndLeadsToTheTopics()
    {
        var page = Render<GoalWizard>();
        SaveFreeGoal(page);

        var saved = page.Find("[role=status] .goal-saved");
        Assert.Contains(FeedbackTexts.Goal.Saved, saved.TextContent);
        Assert.DoesNotContain(FeedbackTexts.Goal.NotFirst, saved.TextContent);
        Assert.Equal(["themen", "lernziele"], page.FindAll(".state-actions a").Select(link => link.GetAttribute("href")));
        Assert.Empty(page.FindAll("form"));
    }

    // A second goal stays next to the first one, which plan, sessions and texts follow for now.
    [Fact]
    public void LaterGoal_SaysThePlanFollowsTheFirst()
    {
        goals.Create(CallOutcome<LearningGoalInfo>.Ok(new LearningGoalInfo(Guid.NewGuid(), "FreeLearning", "C#", null, null, null, "de", null, DateTime.UnixEpoch, InPlan: false)));
        var page = Render<GoalWizard>();
        SaveFreeGoal(page);

        Assert.Contains(FeedbackTexts.Goal.NotFirst, page.Find("[role=status] .goal-saved").TextContent);
    }

    [Fact]
    public void SaveFailing_ShowsTheProblem_AndSavingAgainWorks()
    {
        goals.Create(CallOutcome<LearningGoalInfo>.Failed(TraceId))
            .Create(CallOutcome<LearningGoalInfo>.Ok(new LearningGoalInfo(Guid.NewGuid(), "FreeLearning", "C#", null, null, null, "de", null, DateTime.UnixEpoch, InPlan: true)));
        var page = Render<GoalWizard>();
        SaveFreeGoal(page);

        var problem = page.Find("[role=status] .connection-problem");
        Assert.Contains(FeedbackTexts.Goal.SaveFailed, problem.TextContent);
        Assert.Contains(TraceId, problem.QuerySelector(".request-id")!.TextContent);

        Next(page);

        Assert.NotNull(page.Find("[role=status] .goal-saved"));
        Assert.Equal(2, goals.Created.Count);
    }

    // The login ran out on saving: what was entered waits for the same account and comes back after the login.
    [Fact]
    public async Task SignedOut_KeepsTheInput_ForAfterTheLogin()
    {
        goals.Create(CallOutcome<LearningGoalInfo>.SignedOut);
        var page = Render<GoalWizard>();
        FillExam(page);
        Next(page);
        Next(page);
        page.Find("#goal-topic-1").Change("Zellatmung");
        Next(page);
        Next(page);
        Assert.Contains(FeedbackTexts.Goal.Kept, page.Find("[role=status]").TextContent);
        Assert.Empty(topics.Added);

        await DisposeComponentsAsync();
        var back = Render<GoalWizard>();

        Assert.Contains(FeedbackTexts.Goal.DraftBack, back.Find("[role=status]").TextContent);
        Assert.Contains("Schritt 5 von 5", back.Find("h2").TextContent);
        Back(back);
        Assert.Equal("Zellatmung", back.Find("#goal-topic-1").GetAttribute("value"));
        Back(back);
        Back(back);
        Assert.Equal("Biologie-Klausur", back.Find("#goal-name").GetAttribute("value"));
    }

    // #317 AC4: no topic entered, none is made.
    [Fact]
    public void Topics_LeftEmpty_AddNone()
    {
        var page = Render<GoalWizard>();
        ToTopics(page);
        page.Find("#goal-topic-1").Change("   ");
        Next(page);

        Assert.Contains(FeedbackTexts.Goal.NoAnswer, Squeeze(page.Find(".goal-summary").TextContent));
        Next(page);

        Assert.NotNull(page.Find("[role=status] .goal-saved"));
        Assert.Empty(topics.Added);
        Assert.DoesNotContain(FeedbackTexts.Goal.TopicsAdded, page.Find("[role=status]").TextContent);
    }

    // #317 AC1 and AC2: each name once, after the rule of Knowledge, into the goal just saved. Empty fields don't count.
    [Fact]
    public void Topics_GoToTheNewGoal_EachNameOnce()
    {
        var goalId = Guid.NewGuid();
        goals.Create(CallOutcome<LearningGoalInfo>.Ok(Goal(goalId)));
        var page = Render<GoalWizard>();
        ToTopics(page);
        page.Find("#goal-topic-1").Change(" pH-Wert ");
        More(page);
        page.Find("#goal-topic-2").Change("PH  wert");
        More(page);
        More(page);
        page.Find("#goal-topic-4").Change("Zellatmung");
        Next(page);

        Assert.Equal(["pH-Wert", "Zellatmung"], page.FindAll(".goal-topics li").Select(item => item.TextContent));
        Next(page);

        Assert.Equal([(goalId, "pH-Wert"), (goalId, "Zellatmung")], topics.Added);
        Assert.Single(goals.Created);
        Assert.Contains(FeedbackTexts.Goal.TopicsAdded, page.Find("[role=status] .goal-saved").TextContent);
    }

    [Fact]
    public void MoreTopic_AddsAField_AndPutsTheFocusInIt()
    {
        var page = Render<GoalWizard>();
        ToTopics(page);
        Assert.Equal(FeedbackTexts.Goal.TopicsHint, page.Find("#goal-topic-1-hint").TextContent);
        Assert.Equal("button", page.Find(".goal-more").GetAttribute("type"));

        More(page);

        Assert.Equal("Thema 2", page.Find("label[for=goal-topic-2]").TextContent.Trim());
        Assert.Empty(page.FindAll("#goal-topic-2-hint"));
        AssertFocused(page, "#goal-topic-2");
    }

    [Fact]
    public void TopicTooLong_IsSaidAtItsField()
    {
        var page = Render<GoalWizard>();
        ToTopics(page);
        More(page);
        var second = ReferenceId(page.Find("#goal-topic-2"));
        page.Find("#goal-topic-2").Change(new string('a', TopicValues.NameMaxLength + 1));

        Next(page);

        Assert.Contains("Schritt 4 von 5", page.Find("h2").TextContent);
        Assert.Equal(FeedbackTexts.Goal.TopicTooLong, ErrorAt(page, "#goal-topic-2-error"));
        Assert.Equal("true", page.Find("#goal-topic-2").GetAttribute("aria-invalid"));
        AssertFocused(second);

        page.Find("#goal-topic-2").Change(new string('a', TopicValues.NameMaxLength));
        Next(page);
        Assert.Contains("Schritt 5 von 5", page.Find("h2").TextContent);
    }

    // A topic of that name is already in the goal: that is what the user wanted.
    [Fact]
    public void TopicAlreadyThere_IsNoError()
    {
        topics.Answer(CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: true)));
        var page = Render<GoalWizard>();
        SaveWithTopics(page, "Zellatmung");

        Assert.Contains(FeedbackTexts.Goal.TopicsAdded, page.Find("[role=status] .goal-saved").TextContent);
        Assert.Empty(page.FindAll(".connection-problem, .goal-topics-rejected, .goal-topics-missing"));
        Assert.Empty(page.FindAll(".state-actions button"));
    }

    [Fact]
    public void TopicRefused_SaysWhy_AndOffersNoRetry()
    {
        topics.Answer(new CallOutcome<TopicAdded>(CallResult.Rejected, Messages: ["Der Name ist leer. Gib dem Thema einen Namen."]));
        var page = Render<GoalWizard>();
        SaveWithTopics(page, "Zellatmung");

        Assert.NotNull(page.Find("[role=status] .goal-saved"));
        Assert.Equal(
            "Zellatmung: Der Name ist leer. Gib dem Thema einen Namen.",
            page.Find("[role=status] .goal-topics-rejected li").TextContent);
        Assert.Empty(page.FindAll(".state-actions button"));
    }

    // The goal is saved, a topic didn't get through. Trying again sends only what is missing, and no second goal.
    [Fact]
    public void TopicFailing_IsSaidHonestly_AndTryingAgainSendsOnlyTheMissing()
    {
        topics.Answer(CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: false)))
            .Answer(CallOutcome<TopicAdded>.Failed(TraceId));
        var page = Render<GoalWizard>();
        SaveWithTopics(page, "Zellatmung", "Fotosynthese", "Enzyme");

        var problem = page.Find("[role=status] .connection-problem");
        Assert.Contains(FeedbackTexts.Goal.TopicsFailed, problem.TextContent);
        Assert.Equal(["Fotosynthese", "Enzyme"], problem.QuerySelectorAll("li").Select(item => item.TextContent));
        Assert.Contains(TraceId, problem.QuerySelector(".request-id")!.TextContent);
        Assert.Equal(["Zellatmung", "Fotosynthese"], topics.Added.Select(added => added.Name));

        page.Find(".state-actions button").Click();

        Assert.Equal(["Zellatmung", "Fotosynthese", "Fotosynthese", "Enzyme"], topics.Added.Select(added => added.Name));
        Assert.Single(goals.Created);
        Assert.Empty(page.FindAll(".connection-problem"));
        Assert.Empty(page.FindAll(".state-actions button"));
        Assert.Contains(FeedbackTexts.Goal.TopicsAdded, page.Find("[role=status] .goal-saved").TextContent);
    }

    // Without a login trying again can't work. The shell leads to the login, the page says what is missing.
    [Fact]
    public void LoginEndingOnTheTopics_SaysWhatIsMissing()
    {
        topics.Answer(CallOutcome<TopicAdded>.SignedOut);
        var page = Render<GoalWizard>();
        SaveWithTopics(page, "Zellatmung", "Enzyme");

        var missing = page.Find("[role=status] .goal-topics-missing");
        Assert.Contains(FeedbackTexts.Goal.TopicsSignedOut, missing.TextContent);
        Assert.Equal(["Zellatmung", "Enzyme"], missing.QuerySelectorAll("li").Select(item => item.TextContent));
        Assert.Single(topics.Added);
        Assert.Empty(page.FindAll(".state-actions button"));
    }

    private static LearningGoalInfo Goal(Guid id) =>
        new(id, "FreeLearning", "C#", null, null, null, "de", null, DateTime.UnixEpoch, InPlan: true);

    // A free goal, up to its step of topics.
    private static void ToTopics(IRenderedComponent<GoalWizard> page)
    {
        Choose(page, LearningGoalValues.FreeLearning);
        Next(page);
        page.Find("#goal-name").Change("C#");
        Next(page);
        Next(page);
    }

    private static void SaveWithTopics(IRenderedComponent<GoalWizard> page, params string[] names)
    {
        ToTopics(page);
        for (var i = 0; i < names.Length; i++)
        {
            if (i > 0)
            {
                More(page);
            }

            page.Find($"#goal-topic-{i + 1}").Change(names[i]);
        }

        Next(page);
        Next(page);
    }

    private static void More(IRenderedComponent<GoalWizard> page) => page.Find(".goal-more").Click();

    private static void FillExam(IRenderedComponent<GoalWizard> page)
    {
        Choose(page, LearningGoalValues.ExamPreparation);
        Next(page);
        page.Find("#goal-name").Change("Biologie-Klausur");
        page.Find("#goal-exam-type").Change("Klausur");
        page.Find("#goal-exam-date").Change("2027-03-01");
        page.Find("#goal-goal").Change("eine 2");
    }

    private static void SaveFreeGoal(IRenderedComponent<GoalWizard> page)
    {
        Choose(page, LearningGoalValues.FreeLearning);
        Next(page);
        page.Find("#goal-name").Change("C#");
        Next(page);
        Next(page);
        Next(page);
        Next(page);
    }

    private static void Choose(IRenderedComponent<GoalWizard> page, string kind) => page.Find($"#goal-kind-{kind}").Change(kind);

    private static void Next(IRenderedComponent<GoalWizard> page) => page.Find("form").Submit();

    private static void Back(IRenderedComponent<GoalWizard> page) => page.Find(".goal-back").Click();

    private void AssertFocused(IRenderedComponent<GoalWizard> page, string selector)
    {
        AssertFocused(ReferenceId(page.Find(selector)));
    }

    // bUnit marks an element with its reference only when it is rendered new, so a kept field is looked up before.
    private void AssertFocused(string referenceId)
    {
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal));
        Assert.Equal(referenceId, Assert.IsType<ElementReference>(focus.Arguments[0]).Id);
    }

    private static string ReferenceId(IElement element) =>
        element.Attributes.Single(attribute => attribute.Name.Equals("blazor:elementReference", StringComparison.OrdinalIgnoreCase)).Value;

    // The message without the word for screen readers in front ("Fehler: ").
    private static string ErrorAt(IRenderedComponent<GoalWizard> page, string selector)
    {
        var error = page.Find(selector);
        Assert.Equal("Fehler: ", error.QuerySelector(".visually-hidden")!.TextContent);
        return Squeeze(error.TextContent)["Fehler: ".Length..];
    }

    private static string Squeeze(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}