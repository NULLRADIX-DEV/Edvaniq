using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #357: a multiple choice question has checkboxes and says that several answers can be right. The answer sends all
// ticks, the grade marks each option in words: right and chosen, wrong and chosen, right but not chosen. Assessment
// grades all or nothing, so the page names no partial score.
public sealed class MultipleChoiceTests : BunitContext
{
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Attempt = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");

    private static readonly AskedQuestion Question = new(
        Attempt,
        Topic,
        Guid.NewGuid(),
        "Was braucht die Photosynthese?",
        ["Licht", "Sauerstoff", "Wasser", "Zucker"],
        2,
        QuestionTypes.MultipleChoice);

    private static readonly AskedQuestion SingleChoice = Question with { Type = QuestionTypes.SingleChoice, Text = "Wo?" };

    private readonly ManualTime time = new();

    public MultipleChoiceTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<TimeProvider>(time);
    }

    [Fact]
    public void Question_HasCheckboxes_AndSaysSeveralCanBeRight()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question));

        Assert.Equal(4, page.FindAll("input[type=checkbox]").Count);
        Assert.Empty(page.FindAll("input[type=radio]"));
        Assert.Equal(FeedbackTexts.Quiz.SeveralRight, page.Find("#quiz-several").TextContent);
        var fieldset = page.Find("fieldset");
        Assert.Equal("quiz-several", fieldset.GetAttribute("aria-describedby"));
        Assert.Equal("Was braucht die Photosynthese?", fieldset.QuerySelector("legend")!.TextContent);
        Assert.All(page.FindAll("input[type=checkbox]"), box => Assert.NotNull(page.Find($"label[for={box.Id}]")));
    }

    [Fact]
    public void Ticks_AreSentTogether_InOrder()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(Graded(chosen: [0, 2], right: [0, 2]));
        var page = RenderPage(quiz);
        time.Advance(TimeSpan.FromSeconds(5));

        page.Find("#option-2").Change(true);
        page.Find("#option-1").Change(true);
        page.Find("#option-0").Change(true);
        page.Find("#option-1").Change(false);
        page.Find("form").Submit();

        var (attempt, options, ms) = Assert.Single(quiz.AnsweredMany);
        Assert.Equal((Attempt, 5000), (attempt, ms));
        Assert.Equal([0, 2], options);
        Assert.Empty(quiz.Answered);
    }

    [Fact]
    public void WithoutATick_SaysWhatIsMissing_AndSendsNothing()
    {
        var quiz = new FakeQuiz().Ask(Question);
        var page = RenderPage(quiz);

        page.Find("#option-1").Change(true);
        page.Find("#option-1").Change(false);
        page.Find("form").Submit();

        Assert.Contains(FeedbackTexts.Quiz.NothingChosen, page.Find("[role=alert]").TextContent);
        Assert.Empty(quiz.AnsweredMany);
    }

    // Right means exactly the right ones.
    [Fact]
    public void AllRight_IsPraised_AndListsTheRightOnes()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question).Answer(Graded(chosen: [0, 2], right: [0, 2])));

        Tick(page, 0, 2);

        Assert.Equal("✓ Richtig! Gut gemacht.", Squeeze(page.Find("#feedback-heading").TextContent));
        Assert.Equal([FeedbackTexts.Quiz.RightChosen], Marks(page, 0));
        Assert.Empty(Marks(page, 1));
        Assert.Equal([FeedbackTexts.Quiz.RightChosen], Marks(page, 2));
        Assert.Equal(["Licht", "Wasser"], page.FindAll(".quiz-right-list li").Select(item => item.TextContent));
        Assert.Contains("Richtige Antworten:", page.Find(".quiz-feedback").TextContent);
    }

    // Every option says in words what it was, so it doesn't hang on color, and a screen reader reads it with the label.
    [Fact]
    public void PartlyRight_MarksEachOption_WithoutAPartialScore()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question).Answer(Graded(chosen: [0, 1], right: [0, 2])));

        Tick(page, 0, 1);

        Assert.Equal("✗ Diesmal nicht richtig.", Squeeze(page.Find("#feedback-heading").TextContent));
        Assert.Equal([FeedbackTexts.Quiz.RightChosen], Marks(page, 0));
        Assert.Equal([FeedbackTexts.Quiz.WrongChosen], Marks(page, 1));
        Assert.Equal([FeedbackTexts.Quiz.RightMissed], Marks(page, 2));
        Assert.Empty(Marks(page, 3));
        Assert.All(page.FindAll(".quiz-mark"), mark => Assert.NotNull(mark.Closest("label")));
        Assert.DoesNotContain(" von ", page.Find(".quiz-feedback").TextContent);
        Assert.Contains("quiz-option-right", page.FindAll(".quiz-options li")[2].ClassName);
        Assert.Contains("quiz-option-own", page.FindAll(".quiz-options li")[1].ClassName);
    }

    // The grade counts, not the ticks: a second answer to the same attempt brings the first.
    [Fact]
    public void StoredGrade_ShowsTheStoredTicks()
    {
        var page = RenderPage(new FakeQuiz().Ask(Question).Answer(Graded(chosen: [3], right: [0, 2])));

        Tick(page, 0, 2);

        Assert.True(page.Find("#option-3").HasAttribute("checked"));
        Assert.False(page.Find("#option-0").HasAttribute("checked"));
        Assert.Equal([FeedbackTexts.Quiz.WrongChosen], Marks(page, 3));
    }

    [Fact]
    public async Task Revealed_MarksAllRightOnes()
    {
        var quiz = new FakeQuiz().Ask(Question)
            .Answer(new GradedAnswer(Attempt, null, false, 0, "Weil.", 1000, Revealed: true, Answers: [0, 2]));
        var page = RenderPage(quiz);

        await page.Find(".quiz-reveal").ClickAsync(new());

        Assert.Equal(["Richtige Antwort"], Marks(page, 0));
        Assert.Empty(Marks(page, 1));
        Assert.Equal(["Richtige Antwort"], Marks(page, 2));
        Assert.Single(quiz.Revealed);
    }

    // A lost answer goes again with the same ticks and the same time.
    [Fact]
    public void LostAnswer_IsSentAgain_WithTheSameTicks()
    {
        var quiz = new FakeQuiz().Ask(Question)
            .Answer(CallOutcome<GradedAnswer>.Failed())
            .Answer(Graded(chosen: [0, 2], right: [0, 2]));
        var page = RenderPage(quiz);
        time.Advance(TimeSpan.FromSeconds(2));

        Tick(page, 0, 2);
        time.Advance(TimeSpan.FromSeconds(9));
        page.Find("form").Submit();

        Assert.Equal(2, quiz.AnsweredMany.Count);
        Assert.All(quiz.AnsweredMany, answer =>
        {
            Assert.Equal([0, 2], answer.Options);
            Assert.Equal(2000, answer.ResponseTimeMs);
        });
        Assert.NotNull(page.Find(".quiz-feedback"));
    }

    // Single choice and true/false stay radios with one option sent, also without a type from an older Assessment.
    [Theory]
    [InlineData(QuestionTypes.SingleChoice)]
    [InlineData(QuestionTypes.TrueFalse)]
    [InlineData(null)]
    public void SingleChoiceAndTrueFalse_StayRadios(string? type)
    {
        var quiz = new FakeQuiz().Ask(SingleChoice with { Type = type }).Answer(new GradedAnswer(Attempt, 1, true, 1, "Weil.", 0));
        var page = RenderPage(quiz);

        Assert.Equal(4, page.FindAll("input[type=radio]").Count);
        Assert.Empty(page.FindAll("input[type=checkbox]"));
        Assert.Empty(page.FindAll("#quiz-several"));
        Assert.False(page.Find("fieldset").HasAttribute("aria-describedby"));

        page.Find("#option-1").Change("1");
        page.Find("form").Submit();

        Assert.Equal(1, Assert.Single(quiz.Answered).Option);
        Assert.Empty(quiz.AnsweredMany);
        Assert.Equal(["Deine Antwort", "Richtige Antwort"], Marks(page, 1));
    }

    // A kind of question this app doesn't know is not shown as a question it could be graded wrong on.
    [Fact]
    public async Task UnknownType_SaysSo_AndGoesOnWithTheNextQuestion()
    {
        var quiz = new FakeQuiz().Ask(Question with { Type = "ordering" }).Ask(Question);
        var page = RenderPage(quiz);

        Assert.Contains(FeedbackTexts.Quiz.UnknownType, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll("form"));
        Assert.Empty(page.FindAll("input"));

        await page.Find(".quiz-actions button").ClickAsync(new());

        Assert.Equal(2, quiz.Asked.Count);
        Assert.Equal(4, page.FindAll("input[type=checkbox]").Count);
        Assert.Empty(quiz.AnsweredMany);
        Assert.Empty(quiz.Answered);
    }

    private IRenderedComponent<Practice> RenderPage(FakeQuiz quiz)
    {
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(new TopicInfo(Topic, "Photosynthese", [])));
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));
    }

    private static void Tick(IRenderedComponent<Practice> page, params int[] options)
    {
        foreach (var option in options)
        {
            page.Find($"#option-{option}").Change(true);
        }

        page.Find("form").Submit();
    }

    private static IEnumerable<string> Marks(IRenderedComponent<Practice> page, int option) =>
        page.FindAll(".quiz-options li")[option].QuerySelectorAll(".quiz-mark")
            .Select(mark => Squeeze(mark.TextContent).TrimStart('✓', '✗', ' '));

    private static string Squeeze(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static GradedAnswer Graded(IReadOnlyList<int> chosen, IReadOnlyList<int> right) =>
        new(Attempt, null, chosen.SequenceEqual(right), right[0], "Licht und Wasser.", 3000, Options: chosen, Answers: right);
}