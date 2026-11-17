using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #358: the unit for a new topic. The summary, then its check questions, then how it went. Without check questions it
// ends with the summary, and it never gets stuck on a summary that doesn't come.
public sealed class NewTopicUnitTests : BunitContext
{
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Session = Guid.Parse("3a4b5c6d-7e8f-4a9b-8c0d-1e2f3a4b5c63");
    private static readonly Guid First = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Second = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02");

    private static readonly AskedQuestion SingleQuestion = new(
        First, Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden"], 1, QuestionTypes.SingleChoice);

    private static readonly AskedQuestion TrueFalseQuestion = new(
        Second, Topic, Guid.NewGuid(), "Die Lichtreaktion braucht Licht.", ["Wahr", "Falsch"], 1, QuestionTypes.TrueFalse);

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly List<NewTopicUnitResult> finished = [];

    public NewTopicUnitTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<TimeProvider>(new ManualTime());
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(new TopicInfo(Topic, "Photosynthese", [])));
        Services.AddSingleton(new SummaryPolling(Interval: TimeSpan.FromMilliseconds(10), Limit: TimeSpan.FromMilliseconds(60)));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // AK1: the summary first, the check questions only after "Weiter", then the result with the tally for the runner.
    [Fact]
    public void WithPool_Summary_ThenChecks_ThenTheResult()
    {
        var quiz = new FakeQuiz()
            .Answer(new GradedAnswer(First, 1, true, 1, "Thylakoide.", 1000))
            .Answer(new GradedAnswer(Second, null, false, 0, "Ja.", 1000));
        var checks = new FakeCheckQuestions().Start(Checks(SingleQuestion, TrueFalseQuestion));
        var view = RenderView(checks, quiz, Summaries());

        Assert.Equal("Schritt 1 von 3: Zusammenfassung", view.Find(".unit-step").TextContent);
        Assert.Equal("Zusammenfassung zu „Photosynthese“", view.Find("h2").TextContent);
        Assert.Equal(["Standardtext."], view.FindAll(".summary-text p").Select(paragraph => paragraph.TextContent));
        Assert.Empty(checks.Started);
        Assert.Empty(view.FindAll("legend"));

        view.Find(".unit-actions button").Click();

        Assert.Equal([(Topic, (Guid?)Session)], checks.Started);
        Assert.Equal("Schritt 2 von 3: Checkfragen", view.Find(".unit-step").TextContent);
        Assert.Empty(view.FindAll(".summary-text"));
        Assert.Equal("Frage 1 von 2", view.Find(".block-quiz-progress").TextContent);
        view.Find("#option-1").Change("1");
        view.Find("form").Submit();
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();

        Assert.Equal("Frage 2 von 2", view.Find(".block-quiz-progress").TextContent);
        view.Find(".quiz-reveal").Click();
        Assert.Empty(finished);
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();

        Assert.Equal("Schritt 3 von 3: Ergebnis", view.Find(".unit-step").TextContent);
        Assert.Equal(FeedbackTexts.NewTopicUnit.Done, view.Find("#unit-done-heading").TextContent);
        Assert.Equal(FeedbackTexts.NewTopicUnit.SummaryRead, view.Find(".unit-summary-line").TextContent);
        Assert.Equal("1 von 2 Checkfragen richtig.", view.Find(".unit-tally").TextContent);
        Assert.Equal(new NewTopicUnitResult(true, new QuizTally(1, 2, 2)), Assert.Single(finished));
        Assert.Equal([(First, 1, 0)], quiz.Answered);
        Assert.Equal(Second, Assert.Single(quiz.Revealed).AttemptId);
        Assert.Single(checks.Started);
    }

    // AK2: no check questions yet is a hint after the summary, and the unit ends with the summary alone.
    [Fact]
    public void WithoutPool_IsAHint_AndTheUnitEndsWithTheSummary()
    {
        var checks = new FakeCheckQuestions().Start(new CheckQuestions(Topic, false, []));
        var view = RenderView(checks, new FakeQuiz(), Summaries());

        view.Find(".unit-actions button").Click();

        Assert.Equal(FeedbackTexts.NewTopicUnit.NoChecks, view.Find("#unit-no-checks-heading").TextContent);
        Assert.Contains(FeedbackTexts.NewTopicUnit.NoChecksHint, view.Find(".unit-no-checks").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("legend"));
        Assert.Empty(finished);
        Assert.Equal(FeedbackTexts.NewTopicUnit.Finish, view.Find(".unit-no-checks button").TextContent);
        view.Find(".unit-no-checks button").Click();

        Assert.Equal(FeedbackTexts.NewTopicUnit.NoTally, view.Find(".unit-tally").TextContent);
        Assert.Equal(new NewTopicUnitResult(true, null), Assert.Single(finished));
        Assert.Single(checks.Started);
    }

    // Ready with no questions is no pool either.
    [Fact]
    public void ReadyWithoutQuestions_IsNoPoolEither()
    {
        var view = RenderView(new FakeCheckQuestions().Start(new CheckQuestions(Topic, true, [])), new FakeQuiz(), Summaries());

        view.Find(".unit-actions button").Click();

        Assert.Equal(FeedbackTexts.NewTopicUnit.NoChecks, view.Find("#unit-no-checks-heading").TextContent);
    }

    // The AI could not write the summary: the way on stays, and the result says the summary was missing.
    [Fact]
    public void SummaryFailed_DoesNotHoldTheUnitUp()
    {
        var summaries = new FakeTopicSummaries()
            .OnRequest(SummaryLength.Standard, CallOutcome<TopicSummary>.Ok(new TopicSummary(Topic, SummaryLength.Standard, SummaryStatus.Failed, null, null, true, [])));
        var checks = new FakeCheckQuestions().Start(new CheckQuestions(Topic, false, []));
        var view = RenderView(checks, new FakeQuiz(), summaries);

        Assert.Empty(view.FindAll(".summary-text"));
        view.Find(".unit-actions button").Click();
        view.Find(".unit-no-checks button").Click();

        Assert.Equal(FeedbackTexts.NewTopicUnit.SummaryMissing, view.Find(".unit-summary-line").TextContent);
        Assert.Equal(new NewTopicUnitResult(false, null), Assert.Single(finished));
    }

    // A summary still being written doesn't hold it up either: going on before it is there counts as not read.
    [Fact]
    public void SummaryStillComing_TheWayOnIsThere()
    {
        var summaries = new FakeTopicSummaries { RequestGate = new() }.OnRequest(Ready("Standardtext."));
        var checks = new FakeCheckQuestions().Start(new CheckQuestions(Topic, false, []));
        var view = RenderView(checks, new FakeQuiz(), summaries);

        view.Find(".unit-actions button").Click();
        view.Find(".unit-no-checks button").Click();

        Assert.Equal(new NewTopicUnitResult(false, null), Assert.Single(finished));
    }

    // A failed load offers a retry, which asks once more. Attempts a lost answer made stay open, nobody answers them.
    [Fact]
    public void ChecksFailed_OffersARetry()
    {
        var checks = new FakeCheckQuestions()
            .Start(CallOutcome<CheckQuestions>.Failed("4bf92f3577b34da6a3ce929d0e0e4736"))
            .Start(Checks(SingleQuestion));
        var view = RenderView(checks, new FakeQuiz(), Summaries());

        view.Find(".unit-actions button").Click();

        Assert.Contains(FeedbackTexts.NewTopicUnit.ChecksFailed, view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Contains("4bf92f3577b34da6a3ce929d0e0e4736", view.Markup, StringComparison.Ordinal);
        Assert.Equal("Schritt 2 von 3: Checkfragen", view.Find(".unit-step").TextContent);
        view.Find(".state-actions button").Click();

        Assert.Equal("Frage 1 von 1", view.Find(".block-quiz-progress").TextContent);
        Assert.Equal(2, checks.Started.Count);
        Assert.Empty(finished);
    }

    // 404 from check-questions: the topic is gone or not the user's. The unit says so and doesn't finish.
    [Fact]
    public void ChecksNotFound_IsGone()
    {
        var checks = new FakeCheckQuestions().Start(new CallOutcome<CheckQuestions>(CallResult.NotFound));
        var view = RenderView(checks, new FakeQuiz(), Summaries());

        view.Find(".unit-actions button").Click();

        Assert.Contains(FeedbackTexts.Quiz.TopicGone, view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".unit-step"));
        Assert.NotNull(view.Find(".state-actions a[href=themen]"));
        Assert.Empty(finished);
    }

    // Content doesn't know the topic either: the unit is gone before any check question was made.
    [Fact]
    public void SummaryNotFound_IsGone()
    {
        var summaries = new FakeTopicSummaries().OnRequest(SummaryLength.Standard, new CallOutcome<TopicSummary>(CallResult.NotFound));
        var checks = new FakeCheckQuestions();
        var view = RenderView(checks, new FakeQuiz(), summaries);

        view.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Quiz.TopicGone, view.Find("[role=status]").TextContent, StringComparison.Ordinal), Wait);
        Assert.Empty(view.FindAll(".unit-actions"));
        Assert.Empty(checks.Started);
    }

    // Every call makes new attempts: not while loading, not again on a render, not again after the end.
    [Fact]
    public void CheckQuestions_AreAskedForOnce()
    {
        var checks = new FakeCheckQuestions { Gate = new() }.Start(Checks(SingleQuestion));
        var view = RenderView(checks, Revealing(), Summaries());

        // A double click: the first takes the button away before the second arrives, so there is nothing to click.
        var goOn = view.Find(".unit-actions button");
        goOn.Click();
        Assert.Throws<ElementRemovedFromDomException>(() => goOn.Click());

        Assert.Contains(FeedbackTexts.NewTopicUnit.LoadingChecks, view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".unit-actions button"));
        view.Render();
        Assert.Single(checks.Started);

        checks.Gate.SetResult();
        view.WaitForAssertion(() => Assert.Equal("Frage 1 von 1", view.Find(".block-quiz-progress").TextContent), Wait);
        view.Render();
        view.Find(".quiz-reveal").Click();
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();
        view.Render();

        Assert.Single(finished);
        Assert.Single(checks.Started);
    }

    // For a screen reader: nothing on the first load, then the step while loading, the question, the hint and the result.
    [Fact]
    public void Focus_FollowsEachStep()
    {
        var checks = new FakeCheckQuestions().Start(CallOutcome<CheckQuestions>.Failed()).Start(Checks(SingleQuestion));
        var view = RenderView(checks, Revealing(), Summaries());
        Assert.Empty(Focused());

        view.Find(".unit-actions button").Click();
        Assert.Equal(ReferenceId(view.Find(".unit-step")), Focused().Last());

        var before = Focused().Count;
        view.Find(".state-actions button").Click();
        Assert.Equal(ReferenceId(view.Find("legend")), Focused().Last());
        Assert.True(Focused().Count > before);

        view.Find(".quiz-reveal").Click();
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();
        Assert.Equal(ReferenceId(view.Find("#unit-done-heading")), Focused().Last());
    }

    [Fact]
    public void Focus_GoesToTheHint_ThenToTheResult()
    {
        var view = RenderView(new FakeCheckQuestions().Start(new CheckQuestions(Topic, false, [])), new FakeQuiz(), Summaries());

        view.Find(".unit-actions button").Click();
        Assert.Equal(ReferenceId(view.Find("#unit-no-checks-heading")), Focused().Last());

        view.Find(".unit-no-checks button").Click();
        Assert.Equal(ReferenceId(view.Find("#unit-done-heading")), Focused().Last());
    }

    private IRenderedComponent<NewTopicUnitView> RenderView(FakeCheckQuestions checks, FakeQuiz quiz, FakeTopicSummaries summaries)
    {
        Services.AddSingleton<ICheckQuestions>(checks);
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddSingleton<ITopicSummaries>(summaries);
        return Render<NewTopicUnitView>(parameters => parameters
            .Add(p => p.TopicId, Topic)
            .Add(p => p.SessionId, Session)
            .Add(p => p.OnFinished, result => finished.Add(result)));
    }

    // Grades the first question shown as revealed.
    private static FakeQuiz Revealing() => new FakeQuiz().Answer(new GradedAnswer(First, null, false, 1, "Thylakoide.", 1000));

    private static FakeTopicSummaries Summaries() => new FakeTopicSummaries().OnRequest(Ready("Standardtext."));

    private static TopicSummary Ready(string text) => new(Topic, SummaryLength.Standard, SummaryStatus.Ready, text, null, true, []);

    private static CheckQuestions Checks(params AskedQuestion[] questions) => new(Topic, true, questions);

    private List<string> Focused() =>
        [.. JSInterop.Invocations
            .Where(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal))
            .Select(invocation => Assert.IsType<ElementReference>(invocation.Arguments[0]).Id)];

    private static string ReferenceId(AngleSharp.Dom.IElement element)
    {
        var id = element.Attributes
            .Single(attribute => attribute.Name.Equals("blazor:elementReference", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.NotEmpty(id);
        return id;
    }
}