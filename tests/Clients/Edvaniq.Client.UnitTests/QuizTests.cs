using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// The user picks a topic, answers its question and sees right or wrong, the solution and the explanation at once.
public sealed class QuizTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Subtopic = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    private static readonly Guid Attempt = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid NextAttempt = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02");

    private static readonly AskedQuestion Question = new(
        Attempt, Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden", "Im Cytoplasma"], 1);

    private static readonly AskedQuestion Next = Question with { Id = NextAttempt, Text = "Was entsteht bei der Photosynthese?" };

    private readonly ManualTime time = new();

    public QuizTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<TimeProvider>(time);
    }

    [Fact]
    public void Question_IsShownWithItsOptions_AndWithoutSolution()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question));

        Assert.Equal("Frage zu „Photosynthese“", page.Find("h1").TextContent);
        Assert.Equal("Wo findet die Lichtreaktion statt?", page.Find("legend").TextContent);
        Assert.Equal(["Im Zellkern", "In den Thylakoiden", "Im Cytoplasma"], page.FindAll(".quiz-option-text").Select(option => option.TextContent));
        Assert.Equal(3, page.FindAll("input[type=radio]").Count);
        Assert.Equal("Antwort prüfen", page.Find("button[type=submit]").TextContent);
        Assert.Empty(page.FindAll(".quiz-feedback"));
        Assert.Empty(page.FindAll(".quiz-mark"));
    }

    // A subtopic is a topic too, its name heads the page.
    [Fact]
    public void Subtopic_IsNamed()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question with { TopicId = Subtopic }), Subtopic);

        Assert.Equal("Frage zu „Lichtreaktion“", page.Find("h1").TextContent);
    }

    [Fact]
    public void WithoutTheTopicsName_TheQuestionStillComes()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question), material: new FakeLearningMaterial().Topics(CallOutcome<IReadOnlyList<TopicInfo>>.Failed()));

        Assert.Equal("Frage üben", page.Find("h1").TextContent);
        Assert.Equal("Wo findet die Lichtreaktion statt?", page.Find("legend").TextContent);
    }

    // AC4: right is said so and named, not only colored.
    [Fact]
    public void RightAnswer_IsPraised_WithSolutionAndExplanation()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(option: 1, answer: 1));
        var page = RenderPage(quiz);

        Answer(page, 1);

        var feedback = page.Find(".quiz-feedback");
        Assert.Equal("✓ Richtig! Gut gemacht.", Squeeze(page.Find("#feedback-heading").TextContent));
        Assert.Contains("Richtige Antwort: In den Thylakoiden", Squeeze(feedback.TextContent));
        Assert.Contains("Erklärung: Die Lichtreaktion läuft an den Membranen der Thylakoide ab.", Squeeze(feedback.TextContent));
        Assert.Equal(["Deine Antwort", "Richtige Antwort"], Marks(page, 1));
        Assert.Empty(Marks(page, 0));
        Assert.Empty(page.FindAll("button[type=submit]"));
        Assert.All(page.FindAll("fieldset"), fieldset => Assert.True(fieldset.HasAttribute("disabled")));
        Assert.Equal("Nächste Frage", page.Find(".quiz-actions button").TextContent);
    }

    // AC4: wrong is said plainly and kindly, without blame, and the page shows the way to get it next time.
    [Fact]
    public void WrongAnswer_IsEncouraging_AndNamesTheRightOne()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(option: 0, answer: 1));
        var page = RenderPage(quiz);

        Answer(page, 0);

        var feedback = Squeeze(page.Find(".quiz-feedback").TextContent);
        Assert.Equal("✗ Diesmal nicht richtig.", Squeeze(page.Find("#feedback-heading").TextContent));
        Assert.Contains("Mit der Erklärung sitzt es beim nächsten Mal.", feedback);
        Assert.Contains("Richtige Antwort: In den Thylakoiden", feedback);
        Assert.Equal(["Deine Antwort"], Marks(page, 0));
        Assert.Equal(["Richtige Antwort"], Marks(page, 1));
        Assert.DoesNotContain("quiz-right", page.Find(".quiz-feedback").ClassName);
    }

    // AC2: a second click while the first answer is on its way sends nothing.
    [Fact]
    public void DoubleClick_SendsTheAnswerOnce()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(option: 1, answer: 1));
        quiz.AnswerGate = new TaskCompletionSource();
        var page = RenderPage(quiz);
        page.Find("#option-1").Change("1");

        page.Find("form").Submit();
        page.Find("form").Submit();

        Assert.Equal("Wird geprüft …", page.Find("button[type=submit]").TextContent);
        quiz.AnswerGate.SetResult();
        page.WaitForAssertion(() => Assert.NotNull(page.Find(".quiz-feedback")));
        Assert.Single(quiz.Answered);
        Assert.Empty(page.FindAll("button[type=submit]"));
    }

    // AC2: what reaches the server twice anyway gets the stored grade back, and the page shows that one.
    [Fact]
    public void StoredGrade_ShowsTheStoredAnswer_NotTheTickedOne()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(option: 2, answer: 1));
        var page = RenderPage(quiz);

        Answer(page, 0);

        Assert.Equal(["Deine Antwort"], Marks(page, 2));
        Assert.Empty(Marks(page, 0));
        Assert.True(page.Find("#option-2").HasAttribute("checked"));
        Assert.False(page.Find("#option-0").HasAttribute("checked"));
        Assert.Equal("✗ Diesmal nicht richtig.", Squeeze(page.Find("#feedback-heading").TextContent));
    }

    // A lost answer is sent again to the same attempt with the time measured the first time, so the server counts it once.
    [Fact]
    public void LostAnswer_IsSentAgain_ToTheSameAttempt_WithTheSameTime()
    {
        var quiz = new FakeQuiz()
            .Ask(Question)
            .Answer(CallOutcome<GradedAnswer>.Failed(TraceId))
            .Answer(Graded(option: 1, answer: 1));
        var page = RenderPage(quiz);
        time.Advance(TimeSpan.FromSeconds(3));

        Answer(page, 1);

        Assert.Contains("Deine Antwort ist nicht angekommen.", page.Find("[role=alert]").TextContent);
        Assert.Contains(TraceId, page.Find("[role=alert]").TextContent);
        Assert.Equal("Antwort prüfen", page.Find("button[type=submit]").TextContent);

        time.Advance(TimeSpan.FromSeconds(10));
        page.Find("form").Submit();

        Assert.Equal([(Attempt, 1, 3000), (Attempt, 1, 3000)], quiz.Answered);
        Assert.NotNull(page.Find(".quiz-feedback"));
        Assert.Equal("", page.Find("[role=alert]").TextContent.Trim());
    }

    // The client measures from showing the question to sending the answer.
    [Fact]
    public void ResponseTime_RunsFromTheQuestionToTheAnswer()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(option: 1, answer: 1));
        var page = RenderPage(quiz);

        time.Advance(TimeSpan.FromMilliseconds(4250));
        Answer(page, 1);

        Assert.Equal(4250, Assert.Single(quiz.Answered).ResponseTimeMs);
    }

    [Fact]
    public void WithoutAChoice_SaysWhatIsMissing_AndSendsNothing()
    {
        var quiz = new FakeQuiz().Ask(Question);
        var page = RenderPage(quiz);

        page.Find("form").Submit();

        Assert.Contains("Wähl zuerst eine Antwort aus.", page.Find("[role=alert]").TextContent);
        Assert.Empty(quiz.Answered);
    }

    // AC3: questions come some minutes after the topics. Until then the page says so and how to go on, no error.
    [Fact]
    public void NoQuestionsYet_ShowsAHint_NotAnError()
    {
        var quiz = new FakeQuiz().Ask(new CallOutcome<AskedQuestion>(CallResult.NotReady)).Ask(Question);
        var page = RenderPage(quiz);

        Assert.Contains("Zu diesem Thema gibt es noch keine Fragen.", page.Find("[role=status]").TextContent);
        Assert.Contains("ein paar Minuten", page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll("[role=status] .notice-error"));
        Assert.NotNull(page.Find("[role=status] .empty-state"));
        Assert.Equal("themen", page.Find(".state-actions a").GetAttribute("href"));
        Assert.Equal("Anderes Thema wählen", page.Find(".state-actions a").TextContent);

        page.Find(".state-actions button").Click();

        Assert.Equal("Wo findet die Lichtreaktion statt?", page.Find("legend").TextContent);
        AssertFocused(page, "legend");
    }

    [Fact]
    public void UnknownTopic_IsAnError_WithTheWayBack()
    {
        var page = RenderPage(new FakeQuiz().Ask(new CallOutcome<AskedQuestion>(CallResult.NotFound)));

        Assert.Contains("Dieses Thema gibt es nicht oder nicht mehr.", page.Find("[role=status] .quiz-topic-gone.notice-error").TextContent);
        Assert.Equal("Zurück zu den Themen", page.Find(".state-actions a").TextContent);
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void FailedAsk_NamesTheTrace_AndOffersToLoadAgain()
    {
        var quiz = new FakeQuiz().Ask(CallOutcome<AskedQuestion>.Failed(TraceId)).Ask(Question);
        var page = RenderPage(quiz);

        Assert.Contains("Die Frage ließ sich gerade nicht laden.", page.Find(".connection-problem").TextContent);
        Assert.Contains(TraceId, page.Find(".connection-problem").TextContent);
        Assert.Equal(FeedbackTexts.Connection.Retry, page.Find(".state-actions button").TextContent);

        page.Find(".state-actions button").Click();

        Assert.Equal(2, quiz.Asked.Count);
        Assert.NotNull(page.Find("legend"));
    }

    // The shell says it and leads to the login (SessionExpiryTests), the page adds no second hint.
    [Fact]
    public void SignedOut_BeforeAQuestion_LeavesTheHintToTheShell()
    {
        var page = RenderPage(new FakeQuiz().Ask(CallOutcome<AskedQuestion>.SignedOut));

        Assert.Equal("", page.Find("[role=status]").TextContent.Trim());
        Assert.Empty(page.FindAll("form"));
        Assert.Empty(page.FindAll(".connection-problem"));
    }

    // Question, options and explanation come from the AI and are shown as text, never as markup.
    [Fact]
    public void TextFromTheAi_IsShownAsText()
    {
        const string Markup = "<b>fett</b><script>alert(1)</script>";
        var quiz = new FakeQuiz()
            .Ask(Question with { Text = Markup, Options = [Markup, "B"] })
            .Answer(Graded(option: 0, answer: 0) with { Explanation = Markup });
        var page = RenderPage(quiz);

        Answer(page, 0);

        Assert.Equal(Markup, page.Find("legend").TextContent);
        Assert.Contains(Markup, page.Find(".quiz-option-text").TextContent);
        Assert.Contains(Markup, page.Find(".quiz-feedback").TextContent);
        Assert.Empty(page.FindAll("b"));
        Assert.Empty(page.FindAll("script"));
    }

    // The button that sent the answer goes away. The keyboard focus moves to the feedback, not into nowhere.
    [Fact]
    public void AfterTheGrade_TheFocusIsOnTheFeedback()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question).Answer(Graded(option: 1, answer: 1)));

        Answer(page, 1);

        AssertFocused(page, "#feedback-heading");
    }

    // The next question is a new attempt of the same topic, with the focus on the question.
    [Fact]
    public void NextQuestion_AsksAnew_AndFocusesTheQuestion()
    {
        var quiz = new FakeQuiz().Ask(Question).Ask(Next).Answer(Graded(option: 1, answer: 1));
        var page = RenderPage(quiz);
        var question = ReferenceId(page.Find("legend"));
        time.Advance(TimeSpan.FromSeconds(5));
        Answer(page, 1);
        time.Advance(TimeSpan.FromSeconds(20));

        page.Find(".quiz-actions button").Click();

        Assert.Equal([Topic, Topic], quiz.Asked);
        Assert.Equal("Was entsteht bei der Photosynthese?", page.Find("legend").TextContent);
        Assert.Empty(page.FindAll(".quiz-feedback"));
        Assert.Empty(page.FindAll(".quiz-mark"));
        Assert.Empty(page.FindAll("input[checked]"));
        AssertFocused(question);

        time.Advance(TimeSpan.FromSeconds(2));
        page.Find("#option-0").Change("0");
        page.Find("form").Submit();
        Assert.Equal((NextAttempt, 0, 2000), quiz.Answered[1]);
    }

    // "Lösung anzeigen" sends no option but the time measured, and shows the solution as a wrong answer without marking
    // anything as the user's. Whatever was ticked no longer holds the page.
    [Fact]
    public void ShowingTheSolution_GivesSolutionAndExplanation_AsWrong()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(option: 1, answer: 1) with { Option = null, Correct = false, Revealed = true });
        var page = RenderPage(quiz);
        page.Find("#option-0").Change("0");
        Assert.True(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
        time.Advance(TimeSpan.FromSeconds(7));

        page.Find("button.quiz-reveal").Click();

        Assert.Equal((Attempt, 7000), Assert.Single(quiz.Revealed));
        Assert.Empty(quiz.Answered);
        // Nobody answered, so the heading gives the solution instead of a verdict.
        Assert.Equal("✗ Hier ist die Lösung.", Squeeze(page.Find("#feedback-heading").TextContent));
        var feedback = Squeeze(page.Find(".quiz-feedback").TextContent);
        Assert.Contains("Schau dir die Erklärung in Ruhe an, so bleibt sie hängen.", feedback);
        Assert.DoesNotContain("nicht richtig", feedback, StringComparison.Ordinal);
        Assert.Contains("Richtige Antwort: In den Thylakoiden", feedback);
        Assert.Contains("Erklärung: Die Lichtreaktion läuft an den Membranen der Thylakoide ab.", feedback);
        Assert.Equal(["Richtige Antwort"], Marks(page, 1));
        Assert.Empty(Marks(page, 0));
        Assert.Empty(page.FindAll("button.quiz-reveal"));
        Assert.Empty(page.FindAll("button[type=submit]"));
        Assert.False(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
    }

    [Fact]
    public void RevealButton_StandsNextToTheAnswerButton_AndNeedsNoChoice()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(option: 1, answer: 1) with { Option = null, Correct = false, Revealed = true });
        var page = RenderPage(quiz);

        var actions = page.Find("form .quiz-actions");
        Assert.Equal(["Antwort prüfen", "Lösung anzeigen"], actions.QuerySelectorAll("button").Select(button => Squeeze(button.TextContent)));
        Assert.Equal("button", actions.QuerySelector("button.quiz-reveal")!.GetAttribute("type"));

        page.Find("button.quiz-reveal").Click();

        Assert.Single(quiz.Revealed);
        Assert.Empty(page.FindAll("[role=alert] .notice"));
    }

    // While the solution is on its way, a second click or an answer sends nothing more.
    [Fact]
    public void Reveal_WhileSending_SendsOnce()
    {
        var quiz = new FakeQuiz { AnswerGate = new() }.Ask(Question).Answer(Graded(option: 1, answer: 1) with { Option = null, Correct = false, Revealed = true });
        var page = RenderPage(quiz);

        page.Find("button.quiz-reveal").Click();
        Assert.Equal("Wird geladen …", Squeeze(page.Find("button.quiz-reveal").TextContent));
        page.Find("button.quiz-reveal").Click();
        Answer(page, 1);
        quiz.AnswerGate.SetResult();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".quiz-feedback")));
        Assert.Single(quiz.Revealed);
        Assert.Empty(quiz.Answered);
    }

    [Fact]
    public void Reveal_ThatFails_SaysSo_AndCanBeTriedAgain()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(CallOutcome<GradedAnswer>.Failed(TraceId));
        var page = RenderPage(quiz);

        page.Find("button.quiz-reveal").Click();

        Assert.Contains("nicht angekommen", page.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".quiz-feedback"));
        Assert.NotNull(page.Find("button.quiz-reveal"));
    }

    // Prerendered on the server there is nobody to ask.
    [Fact]
    public void Prerendered_AsksNothing()
    {
        var quiz = new FakeQuiz().Ask(Question);
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial());
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var page = Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));

        Assert.Equal("Wird geladen …", page.Find("[role=status] .loading").TextContent);
        Assert.Empty(quiz.Asked);
    }

    private IRenderedComponent<Practice> RenderPage(FakeQuiz quiz, Guid? topic = null, FakeLearningMaterial? material = null)
    {
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddSingleton<ILearningMaterial>(material ?? new FakeLearningMaterial()
            .Topics(new TopicInfo(Topic, "Photosynthese", [new SubtopicInfo(Subtopic, "Lichtreaktion")])));
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Practice>(parameters => parameters.Add(p => p.TopicId, topic ?? Topic));
    }

    private static void Answer(IRenderedComponent<Practice> page, int option)
    {
        page.Find($"#option-{option}").Change(option.ToString(System.Globalization.CultureInfo.InvariantCulture));
        page.Find("form").Submit();
    }

    private void AssertFocused(IRenderedComponent<Practice> page, string selector) =>
        AssertFocused(ReferenceId(page.Find(selector)));

    private void AssertFocused(string referenceId)
    {
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal));
        Assert.Equal(referenceId, Assert.IsType<ElementReference>(focus.Arguments[0]).Id);
    }

    // bUnit marks an element with its reference only when it's rendered new, an element kept by an update loses the value.
    private static string ReferenceId(AngleSharp.Dom.IElement element)
    {
        var id = element.Attributes
            .Single(attribute => attribute.Name.Equals("blazor:elementReference", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.NotEmpty(id);
        return id;
    }

    // The marks next to an option, "Deine Antwort" and "Richtige Antwort", without their symbols.
    private static IEnumerable<string> Marks(IRenderedComponent<Practice> page, int option) =>
        page.FindAll(".quiz-options li")[option].QuerySelectorAll(".quiz-mark")
            .Select(mark => Squeeze(mark.TextContent).TrimStart('✓', '✗', ' '));

    private static string Squeeze(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static GradedAnswer Graded(int option, int answer) =>
        new(Attempt, option, option == answer, answer, "Die Lichtreaktion läuft an den Membranen der Thylakoide ab.", 3000);
}