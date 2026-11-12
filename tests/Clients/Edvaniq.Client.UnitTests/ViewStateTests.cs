using AngleSharp.Dom;

using Bunit;
using Bunit.Rendering;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #220: the three views of the walking skeleton show loading, empty and failed the same way, through the shared parts:
// the state in the view's live region, the next step below it.
public sealed class ViewStateTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");

    private static readonly AskedQuestion Question = new(
        Guid.NewGuid(), Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden"], 1);

    private static readonly TodayPlan Plan = new(new DateOnly(2026, 11, 12), [new PlanBlock(Topic, 20, BlockKind.Learn)], Empty: null, MasteryAvailable: true, [], []);

    private readonly FakeTodayPlan plans = new();
    private readonly FakeLearningMaterial material = new();
    private readonly FakeQuiz quiz = new();

    public ViewStateTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<IReturnSignal>(new FakeReturnSignal());
        Services.AddSingleton<ITodayPlan>(plans);
        Services.AddSingleton<ILearningMaterial>(material);
        Services.AddSingleton<IQuiz>(quiz);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    public enum View
    {
        Plan,
        Material,
        Quiz,
    }

    // AC1: while a view loads, its live region says so, and there is no step to take yet.
    [Theory]
    [InlineData(View.Plan)]
    [InlineData(View.Material)]
    [InlineData(View.Quiz)]
    public void Loading_IsAnnouncedInTheLiveRegion(View view)
    {
        var gate = new TaskCompletionSource();
        plans.Gate = gate;
        material.ListGate = gate;
        quiz.AskGate = gate;
        plans.Answer(Plan);
        quiz.Ask(Question);

        var page = RenderView(view);

        Assert.Equal("Wird geladen …", Status(page).QuerySelector(".loading")!.TextContent);
        Assert.Empty(page.FindAll(".state-actions"));
        Assert.Empty(page.FindAll(".connection-problem"));

        gate.SetResult();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".loading")));
    }

    // AC1: an empty view says what is missing and leads on.
    [Fact]
    public void EmptyPlan_LeadsToTheTopics()
    {
        plans.Answer(Plan with { Blocks = [], Empty = PlanEmpty.NoTopics });

        var page = RenderView(View.Plan);

        Assert.Contains(FeedbackTexts.Plan.NoTopics, EmptyState(page).TextContent);
        var next = page.Find(".state-actions a.button");
        Assert.Equal(FeedbackTexts.Plan.NoTopicsLink, next.TextContent);
        Assert.Equal("themen", next.GetAttribute("href"));
    }

    [Fact]
    public void EmptyMaterial_LeadsToTheFormBelow()
    {
        var page = RenderView(View.Material);

        var empty = EmptyState(page);
        Assert.Contains(FeedbackTexts.Material.Empty, empty.TextContent);
        Assert.Contains(FeedbackTexts.Material.EmptyHint, empty.TextContent);
        Assert.Equal("Ersten Text einfügen", page.Find("#paste-heading").TextContent);
        Assert.NotNull(page.Find("form textarea"));
    }

    [Fact]
    public void QuizWithoutQuestions_OffersToTryAgainOrPickAnotherTopic()
    {
        quiz.Ask(new CallOutcome<AskedQuestion>(CallResult.NotReady)).Ask(Question);

        var page = RenderView(View.Quiz);

        Assert.Contains(FeedbackTexts.Quiz.NoQuestions, EmptyState(page).TextContent);
        Assert.Equal(FeedbackTexts.Quiz.OtherTopic, page.Find(".state-actions a").TextContent);
        Assert.DoesNotContain("button", page.Find(".state-actions a").ClassList);

        page.Find(".state-actions button").Click();

        page.WaitForAssertion(() => Assert.Equal(Question.Text, page.Find("legend").TextContent));
    }

    // AC1 and AC2: a failed load says what didn't work in plain words, the request id stays apart from the sentence, and
    // "Erneut versuchen" below the live region loads again.
    [Theory]
    [InlineData(View.Plan, FeedbackTexts.Plan.LoadFailed)]
    [InlineData(View.Material, FeedbackTexts.Material.LoadFailed)]
    [InlineData(View.Quiz, FeedbackTexts.Quiz.LoadFailed)]
    public void Failure_ShowsTheProblem_AndRetryLoadsAgain(View view, string title)
    {
        plans.Answer(CallOutcome<TodayPlan>.Failed(TraceId)).Answer(Plan);
        material.Documents(CallOutcome<IReadOnlyList<DocumentInfo>>.Failed(TraceId)).Documents();
        quiz.Ask(CallOutcome<AskedQuestion>.Failed(TraceId)).Ask(Question);

        var page = RenderView(view);

        var problem = Status(page).QuerySelector(".connection-problem.notice-error")!;
        Assert.Equal(title, problem.QuerySelector(".notice-title")!.TextContent);
        Assert.Contains(FeedbackTexts.Connection.Advice, problem.TextContent);
        Assert.Equal(TraceId, problem.QuerySelector(".request-id code")!.TextContent);
        Assert.DoesNotContain(TraceId, problem.QuerySelector(".notice-title")!.TextContent);
        Assert.Null(Status(page).QuerySelector("button"));
        var retry = page.Find(".state-actions button");
        Assert.Equal(FeedbackTexts.Connection.Retry, retry.TextContent);

        retry.Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".connection-problem")));
        Assert.Equal(2, view switch
        {
            View.Plan => plans.Calls,
            View.Material => material.DocumentCalls,
            _ => quiz.Asked.Count,
        });
    }

    // AC3: the states are parts of the shared UI, with text and next step as parameters.
    [Fact]
    public void EmptyState_ShowsTitleAndHint_AsAHint()
    {
        var empty = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "Noch nichts da.").Add(p => p.Hint, "So geht es weiter."));

        var root = empty.Find(".empty-state");
        Assert.Contains("notice-info", root.ClassList);
        Assert.Equal("Noch nichts da.", root.QuerySelector(".notice-title")!.TextContent);
        Assert.Equal("So geht es weiter.", root.QuerySelector(".notice-body > p:last-child")!.TextContent);
    }

    [Fact]
    public void StateActions_ALinkAlone_IsTheStep()
    {
        var actions = Render<StateActions>(parameters => parameters.Add(p => p.Href, "themen").Add(p => p.LinkText, "Zu den Themen"));

        Assert.Empty(actions.FindAll("button"));
        Assert.Contains("button", actions.Find("a").ClassList);
    }

    [Fact]
    public void StateActions_ButtonAndLink_RunTheActionOnce()
    {
        var clicks = 0;
        var actions = Render<StateActions>(parameters => parameters
            .Add(p => p.ActionText, "Erneut versuchen").Add(p => p.OnAction, () => clicks++)
            .Add(p => p.Href, "themen").Add(p => p.LinkText, "Zurück"));

        Assert.Equal("button", actions.Find("button").GetAttribute("type"));
        Assert.DoesNotContain("button", actions.Find("a").ClassList);

        actions.Find("button").Click();

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void StateActions_WithoutAStep_ShowsNothingToPress()
    {
        var actions = Render<StateActions>();

        Assert.Empty(actions.FindAll("button, a"));
    }

    private IRenderedComponent<ContainerFragment> RenderView(View view) => Render(builder =>
    {
        builder.OpenComponent(0, view switch
        {
            View.Plan => typeof(TodayPlanView),
            View.Material => typeof(Topics),
            _ => typeof(Practice),
        });
        if (view == View.Quiz)
        {
            builder.AddAttribute(1, nameof(Practice.TopicId), Topic);
        }

        builder.CloseComponent();
    });

    private static IElement Status(IRenderedComponent<ContainerFragment> page) => page.Find("[role=status]");

    private static IElement EmptyState(IRenderedComponent<ContainerFragment> page) => Status(page).QuerySelector(".empty-state")!;
}