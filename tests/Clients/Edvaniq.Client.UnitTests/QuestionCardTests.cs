using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #357: the question as a building block. The caller gives the question, the card sends the answer once, shows the
// feedback and reports the grade.
public sealed class QuestionCardTests : BunitContext
{
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Attempt = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Document = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid Section = Guid.Parse("4d5e6f7a-8b9c-4d0e-1f2a-3b4c5d6e7f80");

    private static readonly AskedQuestion Single = new(
        Attempt, Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden", "Im Cytoplasma"], 1,
        QuestionTypes.SingleChoice, new QuestionSource(Document, Section));

    private static readonly AskedQuestion TrueFalse = Single with
    {
        Text = "Die Lichtreaktion braucht Licht.",
        Options = ["Wahr", "Falsch"],
        Type = QuestionTypes.TrueFalse,
    };

    private static readonly AskedQuestion Multiple = Single with
    {
        Text = "Was braucht die Photosynthese?",
        Options = ["Licht", "Sauerstoff", "Wasser"],
        Type = QuestionTypes.MultipleChoice,
    };

    private readonly ManualTime time = new();
    private readonly List<GradedAnswer> reported = [];
    private int gone;

    public QuestionCardTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<TimeProvider>(time);
        Services.AddSingleton<ISources>(new FakeSources());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // AK1: true/false is two options to pick like single choice, multiple choice has boxes. Every option is a labeled
    // input, so mouse, touch and keyboard reach it the same way.
    [Theory]
    [InlineData(QuestionTypes.SingleChoice, "radio", 3)]
    [InlineData(QuestionTypes.TrueFalse, "radio", 2)]
    [InlineData(QuestionTypes.MultipleChoice, "checkbox", 3)]
    public void EveryType_HasALabeledInputPerOption(string type, string input, int count)
    {
        var question = type switch
        {
            QuestionTypes.TrueFalse => TrueFalse,
            QuestionTypes.MultipleChoice => Multiple,
            _ => Single,
        };

        var card = RenderCard(new FakeQuiz(), question);

        var inputs = card.FindAll($"input[type={input}]");
        Assert.Equal(count, inputs.Count);
        Assert.All(inputs, field => Assert.NotNull(card.Find($"label[for={field.Id}]")));
        Assert.Equal("submit", card.Find("button[type=submit]").GetAttribute("type"));
    }

    // AK3: label and hint at the question from the start, the source with the feedback.
    [Fact]
    public void LabelAndHint_StandAtTheQuestion_TheSourceComesWithTheFeedback()
    {
        var card = RenderCard(new FakeQuiz().Answer(Graded(option: 1, answer: 1)), Single);

        Assert.Equal(FeedbackTexts.Sources.AiGenerated, card.Find(".quiz-form .ai-note .ai-label").TextContent);
        Assert.Equal(FeedbackTexts.Sources.MayErr, card.Find(".quiz-form .ai-note .ai-hint").TextContent);
        Assert.Empty(card.FindAll(".source"));

        Answer(card, 1);

        Assert.Equal(FeedbackTexts.Sources.Heading, card.Find(".quiz-feedback .quiz-source-heading").TextContent);
        Assert.NotNull(card.Find(".quiz-feedback .source"));
    }

    // AK2: a wrong answer and a shown solution are no verdict on the person, both bring solution and explanation.
    [Theory]
    [InlineData(false, FeedbackTexts.Answer.Wrong, FeedbackTexts.Answer.WrongHint)]
    [InlineData(true, FeedbackTexts.Answer.Revealed, FeedbackTexts.Answer.RevealedHint)]
    public void NotRight_IsSaidKindly_WithSolutionAndExplanation(bool reveal, string heading, string hint)
    {
        var graded = reveal ? Graded(option: null, answer: 1) with { Revealed = true } : Graded(option: 0, answer: 1);
        var card = RenderCard(new FakeQuiz().Answer(graded), Single);

        if (reveal)
        {
            card.Find(".quiz-reveal").Click();
        }
        else
        {
            Answer(card, 0);
        }

        var feedback = card.Find(".quiz-feedback");
        Assert.Contains(heading, feedback.QuerySelector("#feedback-heading")!.TextContent, StringComparison.Ordinal);
        Assert.Contains(hint, feedback.TextContent, StringComparison.Ordinal);
        Assert.Contains("Richtige Antwort: In den Thylakoiden", Squeeze(feedback.TextContent), StringComparison.Ordinal);
        Assert.Contains("Erklärung: Weil die Membranen dort sitzen.", Squeeze(feedback.TextContent), StringComparison.Ordinal);
        Assert.Equal(graded, Assert.Single(reported));
    }

    // AK4: a second click while the answer is on its way sends nothing more.
    [Fact]
    public void DoubleClick_SendsTheAnswerOnce()
    {
        var gate = new TaskCompletionSource();
        var quiz = new FakeQuiz { AnswerGate = gate }.Answer(Graded(option: 1, answer: 1));
        var card = RenderCard(quiz, Single);
        card.Find("#option-1").Change("1");

        card.Find("form").Submit();
        card.Find("form").Submit();
        card.Find(".quiz-reveal").Click();
        gate.SetResult();

        card.WaitForAssertion(() => Assert.NotNull(card.Find(".quiz-feedback")));
        Assert.Single(quiz.Answered);
        Assert.Empty(quiz.Revealed);
        Assert.Single(reported);
    }

    // AK4: after a timeout the hint offers a retry, and the retry sends the same answer with the same time. The server
    // counts an attempt once, whatever comes twice.
    [Fact]
    public void RetryAfterATimeout_SendsTheSameAnswer()
    {
        var quiz = new FakeQuiz().Answer(CallOutcome<GradedAnswer>.Failed("4bf92f3577b34da6a3ce929d0e0e4736")).Answer(Graded(option: 1, answer: 1));
        var card = RenderCard(quiz, Single);
        time.Advance(TimeSpan.FromSeconds(4));
        Answer(card, 1);

        Assert.Contains(FeedbackTexts.Quiz.NotThrough, card.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Contains(FeedbackTexts.Quiz.NotThroughHint, card.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Empty(reported);

        time.Advance(TimeSpan.FromSeconds(30));
        card.Find("form").Submit();

        Assert.Equal([(Attempt, 1, 4000), (Attempt, 1, 4000)], quiz.Answered);
        Assert.NotNull(card.Find(".quiz-feedback"));
        Assert.Single(reported);
    }

    // AK4: answered in a second tab before, the server brings back that grade. The card shows what counts, not what was
    // ticked here.
    [Fact]
    public void AnsweredInAnotherTab_ShowsTheStoredGrade()
    {
        var card = RenderCard(new FakeQuiz().Answer(Graded(option: 2, answer: 1)), Single);

        Answer(card, 1);

        Assert.Equal("2", card.Find("input[checked]").GetAttribute("value"));
        Assert.Contains("Deine Antwort", card.FindAll(".quiz-options li")[2].TextContent, StringComparison.Ordinal);
        Assert.Equal(2, Assert.Single(reported).Option);
    }

    // For a screen reader: after the grade the focus is on the feedback, with the next question on the question.
    [Fact]
    public void Focus_GoesToTheFeedback_ThenToTheNextQuestion()
    {
        var card = RenderCard(new FakeQuiz().Answer(Graded(option: 1, answer: 1)), Single);
        var legend = ReferenceId(card.Find("legend"));
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal));

        Answer(card, 1);
        AssertFocused(ReferenceId(card.Find("#feedback-heading")));

        card.Render(parameters => parameters
            .Add(p => p.Question, TrueFalse with { Id = Guid.NewGuid() })
            .Add(p => p.Round, 1)
            .Add(p => p.FocusOnShow, true));

        Assert.Empty(card.FindAll(".quiz-feedback"));
        Assert.Empty(card.FindAll("input[checked]"));
        Assert.Equal("Die Lichtreaktion braucht Licht.", card.Find("legend").TextContent);
        AssertFocused(legend);
    }

    [Fact]
    public void TopicGone_IsReported()
    {
        var card = RenderCard(new FakeQuiz().Answer(new CallOutcome<GradedAnswer>(CallResult.NotFound)), Single);

        Answer(card, 1);

        Assert.Equal(1, gone);
        Assert.Empty(reported);
    }

    private IRenderedComponent<QuestionCard> RenderCard(FakeQuiz quiz, AskedQuestion question)
    {
        Services.AddSingleton<IQuiz>(quiz);
        var material = new FakeLearningMaterial()
            .Documents(new DocumentInfo(Document, "Skript Biologie", DocumentStatus.Ready, DateTimeOffset.UnixEpoch));
        return Render<QuestionCard>(parameters => parameters
            .Add(p => p.Question, question)
            .Add(p => p.TopicId, Topic)
            .Add(p => p.Documents, new DocumentLookup(material))
            .Add(p => p.OnGraded, graded => reported.Add(graded))
            .Add(p => p.OnGone, () => gone++));
    }

    private static void Answer(IRenderedComponent<QuestionCard> card, int option)
    {
        card.Find($"#option-{option}").Change(option.ToString(System.Globalization.CultureInfo.InvariantCulture));
        card.Find("form").Submit();
    }

    private void AssertFocused(string referenceId)
    {
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal));
        Assert.Equal(referenceId, Assert.IsType<ElementReference>(focus.Arguments[0]).Id);
    }

    // bUnit marks an element with its reference only when it's rendered new.
    private static string ReferenceId(AngleSharp.Dom.IElement element)
    {
        var id = element.Attributes
            .Single(attribute => attribute.Name.Equals("blazor:elementReference", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.NotEmpty(id);
        return id;
    }

    private static string Squeeze(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static GradedAnswer Graded(int? option, int answer) =>
        new(Attempt, option, option == answer, answer, "Weil die Membranen dort sitzen.", 4000);
}