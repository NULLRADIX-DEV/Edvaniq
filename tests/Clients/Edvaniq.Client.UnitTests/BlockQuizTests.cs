using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #357: the quiz of a block, question after question with its progress, then how many were right.
public sealed class BlockQuizTests : BunitContext
{
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Block = Guid.Parse("2e3f4a5b-6c7d-4e8f-9a0b-1c2d3e4f5a62");
    private static readonly Guid First = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Second = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02");
    private static readonly Guid Third = Guid.Parse("8d2e3f4a-5b6c-4d7e-8f9a-0b1c2d3e4f03");

    private static readonly BlockQuizQuestion SingleQuestion = new(
        First, Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden"], 1, false, null,
        QuestionTypes.SingleChoice);

    private static readonly BlockQuizQuestion MultipleQuestion = new(
        Second, Topic, Guid.NewGuid(), "Was braucht die Photosynthese?", ["Licht", "Sauerstoff", "Wasser"], 2, false, null,
        QuestionTypes.MultipleChoice);

    private static readonly BlockQuizQuestion TrueFalseQuestion = new(
        Third, Topic, Guid.NewGuid(), "Die Lichtreaktion braucht Licht.", ["Wahr", "Falsch"], 1, false, null, QuestionTypes.TrueFalse);

    private readonly List<QuizTally> finished = [];

    public BlockQuizTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<TimeProvider>(new ManualTime());
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // All three types in a row: the progress counts up, the way on is "Nächste Frage" and at the last "Zum Ergebnis",
    // and the end says how many were right and hands the tally to the caller.
    [Fact]
    public void ThreeTypes_OneAfterAnother_ThenTheTally()
    {
        var quiz = new FakeQuiz()
            .Answer(new GradedAnswer(First, 1, true, 1, "Thylakoide.", 1000))
            .Answer(new GradedAnswer(Second, null, false, 0, "Licht und Wasser.", 1000, Options: [0], Answers: [0, 2]))
            .Answer(new GradedAnswer(Third, 0, true, 0, "Ja.", 1000));
        var blocks = new FakeBlockQuiz().Open(Quiz(SingleQuestion, MultipleQuestion, TrueFalseQuestion));
        var view = RenderView(quiz, blocks);

        Assert.Equal([(Topic, Block, BlockQuizView.DefaultSize)], blocks.Opened);
        AssertProgress(view, 1, 3);
        Assert.Equal(2, view.FindAll("input[type=radio]").Count);
        view.Find("#option-1").Change("1");
        view.Find("form").Submit();
        Assert.Equal("Nächste Frage", view.Find(".quiz-feedback ~ .quiz-actions button").TextContent);
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();

        AssertProgress(view, 2, 3);
        Assert.Equal("Was braucht die Photosynthese?", view.Find("legend").TextContent);
        Assert.Equal(3, view.FindAll("input[type=checkbox]").Count);
        view.Find("#option-0").Change(true);
        view.Find("form").Submit();
        Assert.Equal([(Second, (IReadOnlyList<int>)[0], 0)], quiz.AnsweredMany.Select(answer => (answer.AttemptId, answer.Options, answer.ResponseTimeMs)));
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();

        AssertProgress(view, 3, 3);
        Assert.Equal(2, view.FindAll("input[type=radio]").Count);
        view.Find("#option-0").Change("0");
        view.Find("form").Submit();
        Assert.Equal(FeedbackTexts.BlockQuiz.ToResult, view.Find(".quiz-feedback ~ .quiz-actions button").TextContent);
        Assert.Empty(finished);
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();

        Assert.Empty(view.FindAll("legend"));
        Assert.Equal(FeedbackTexts.BlockQuiz.Done, view.Find("#block-quiz-done-heading").TextContent);
        Assert.Equal("2 von 3 Fragen richtig.", view.Find(".block-quiz-tally").TextContent);
        Assert.Equal(new QuizTally(2, 3, 3), Assert.Single(finished));
        Assert.Equal([(First, 1, 0), (Third, 0, 0)], quiz.Answered);
    }

    // For a screen reader: the first question gets no focus on its own, each next one does, and the end puts it on the
    // tally's heading.
    [Fact]
    public void Focus_FollowsTheUser_ToEachQuestion_AndToTheEnd()
    {
        var quiz = new FakeQuiz().Answer(new GradedAnswer(First, 1, true, 1, "Ja.", 1000));
        var view = RenderView(quiz, new FakeBlockQuiz().Open(Quiz(SingleQuestion, TrueFalseQuestion with { Id = Second })));
        var legend = ReferenceId(view.Find("legend"));
        Assert.Empty(Focused());

        view.Find("#option-1").Change("1");
        view.Find("form").Submit();
        Assert.Equal(ReferenceId(view.Find("#feedback-heading")), Focused().Last());
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();
        Assert.Equal(legend, Focused().Last());

        view.Find(".quiz-reveal").Click();
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();
        Assert.Equal(ReferenceId(view.Find("#block-quiz-done-heading")), Focused().Last());
    }

    // Back to a quiz begun before: the answered questions count and are not asked again.
    [Fact]
    public void BegunBefore_GoesOnWithTheFirstOpenQuestion()
    {
        var quiz = new FakeQuiz().Answer(new GradedAnswer(Second, null, false, 0, "Licht.", 1000, Options: [1], Answers: [0]));
        var view = RenderView(quiz, new FakeBlockQuiz().Open(Quiz(
            SingleQuestion with { Answered = true, Correct = true }, MultipleQuestion, TrueFalseQuestion with { Answered = true, Correct = false })));

        AssertProgress(view, 2, 3);
        view.Find("#option-1").Change(true);
        view.Find("form").Submit();
        Assert.Equal(FeedbackTexts.BlockQuiz.ToResult, view.Find(".quiz-feedback ~ .quiz-actions button").TextContent);
        view.Find(".quiz-feedback ~ .quiz-actions button").Click();

        Assert.Equal("1 von 3 Fragen richtig.", view.Find(".block-quiz-tally").TextContent);
        Assert.Equal(new QuizTally(1, 3, 3), Assert.Single(finished));
    }

    [Fact]
    public void AllAnsweredBefore_ShowsTheTallyAtOnce()
    {
        var view = RenderView(new FakeQuiz(), new FakeBlockQuiz().Open(Quiz(
            SingleQuestion with { Answered = true, Correct = true }, TrueFalseQuestion with { Answered = true, Correct = true })));

        Assert.Equal("2 von 2 Fragen richtig.", view.Find(".block-quiz-tally").TextContent);
        Assert.Equal(new QuizTally(2, 2, 2), Assert.Single(finished));
    }

    // A type this app can't show says so and lets the user go on. It counts as not answered.
    [Fact]
    public void UnknownType_SaysSo_AndGoesOn()
    {
        var view = RenderView(new FakeQuiz(), new FakeBlockQuiz().Open(Quiz(SingleQuestion with { Type = "matching" })));

        Assert.Contains(FeedbackTexts.Quiz.UnknownType, view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll("legend"));
        view.Find(".quiz-actions button").Click();

        Assert.Equal("0 von 1 Fragen richtig.", view.Find(".block-quiz-tally").TextContent);
        Assert.Equal(new QuizTally(0, 0, 1), Assert.Single(finished));
    }

    [Fact]
    public void NoQuestionsYet_IsAHint_AndAskingAgainOpensTheQuiz()
    {
        var blocks = new FakeBlockQuiz().Open(new CallOutcome<BlockQuiz>(CallResult.NotReady)).Open(Quiz(SingleQuestion));
        var view = RenderView(new FakeQuiz(), blocks);

        Assert.Contains(FeedbackTexts.Quiz.NoQuestions, view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        view.Find(".state-actions button").Click();

        Assert.Equal(2, blocks.Opened.Count);
        AssertProgress(view, 1, 1);
    }

    // Opening again is safe: Assessment keeps the quiz of the block and brings the same questions.
    [Fact]
    public void LoadFailed_OffersARetry()
    {
        var blocks = new FakeBlockQuiz().Open(CallOutcome<BlockQuiz>.Failed("4bf92f3577b34da6a3ce929d0e0e4736")).Open(Quiz(SingleQuestion));
        var view = RenderView(new FakeQuiz(), blocks);

        Assert.Contains(FeedbackTexts.BlockQuiz.LoadFailed, view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        view.Find(".state-actions button").Click();

        AssertProgress(view, 1, 1);
        Assert.NotEmpty(Focused());
    }

    [Fact]
    public void UnknownTopic_IsAnError()
    {
        var view = RenderView(new FakeQuiz(), new FakeBlockQuiz().Open(new CallOutcome<BlockQuiz>(CallResult.NotFound)));

        Assert.Contains(FeedbackTexts.Quiz.TopicGone, view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Empty(finished);
    }

    private IRenderedComponent<BlockQuizView> RenderView(FakeQuiz quiz, FakeBlockQuiz blocks)
    {
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddSingleton<IBlockQuiz>(blocks);
        return Render<BlockQuizView>(parameters => parameters
            .Add(p => p.TopicId, Topic)
            .Add(p => p.BlockId, Block)
            .Add(p => p.OnFinished, tally => finished.Add(tally)));
    }

    private static BlockQuiz Quiz(params BlockQuizQuestion[] questions) => new(Guid.NewGuid(), Block, Topic, questions, false);

    private static void AssertProgress(IRenderedComponent<BlockQuizView> view, int at, int of)
    {
        Assert.Equal($"Frage {at} von {of}", view.Find(".block-quiz-progress").TextContent);
        Assert.Equal(at.ToString(System.Globalization.CultureInfo.InvariantCulture), view.Find("progress").GetAttribute("value"));
        Assert.Equal(of.ToString(System.Globalization.CultureInfo.InvariantCulture), view.Find("progress").GetAttribute("max"));
    }

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