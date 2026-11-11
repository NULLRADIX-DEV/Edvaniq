using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #222: mastery and plan follow an answer a moment later, over events. Until then the pages say so instead of showing
// the old state as if the answer had changed nothing, and after the limit they offer to check again.
public sealed class DelayedUpdateTests : BunitContext
{
    // Polling runs on real time, a busy runner needs a while.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // Long enough that the hint is still there when the test looks, short for the tests that wait for the limit.
    private static readonly AnswerSyncTiming Patient = new(TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(30));

    private static readonly AnswerSyncTiming Fast = new(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(300));

    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Other = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");
    private static readonly Guid Attempt = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Earlier = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02");

    private static readonly AskedQuestion Question = new(
        Attempt, Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden"], 1);

    private static readonly TopicInfo[] Topics = [new(Topic, "Photosynthese", []), new(Other, "Zellatmung", [])];

    private readonly FakeReturnSignal signal = new();

    public DelayedUpdateTests()
    {
        Services.AddSingleton<IReturnSignal>(signal);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // AC1 and AC2 on the practice page: the hint first, never the mastery before the answer, then the new one.
    [Fact]
    public void Quiz_ShowsTheHint_ThenTheNewMastery()
    {
        var gate = new GatedProgress(
            new TopicProgress(Topic, 50, TopicState.New, Earlier),
            new TopicProgress(Topic, 62, TopicState.Building, Attempt));
        var page = RenderQuiz(gate, Patient);

        Answer(page);

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Progress.Updating, page.Find(".answer-progress").TextContent), Wait);
        Assert.DoesNotContain("50 %", page.Find(".answer-progress").TextContent);
        gate.Open();
        page.WaitForAssertion(() => Assert.Contains("62 %, im Aufbau", page.Find(".answer-progress").TextContent), Wait);
        Assert.DoesNotContain(FeedbackTexts.Progress.Updating, page.Find(".answer-progress").TextContent);
        Assert.Empty(page.FindAll(".answer-progress button"));
    }

    // AC3: no new state within the limit, a hint with a way to check again, which then brings it.
    [Fact]
    public void Quiz_Late_OffersToCheckAgain()
    {
        var gate = new GatedProgress(
            new TopicProgress(Topic, 50, TopicState.New, Earlier),
            new TopicProgress(Topic, 37, TopicState.Weakness, Attempt));
        var page = RenderQuiz(gate);

        Answer(page);

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Progress.Late, page.Find(".answer-progress").TextContent), Wait);
        Assert.DoesNotContain("50 %", page.Find(".answer-progress").TextContent);
        var check = page.Find(".answer-progress button");
        Assert.Equal(FeedbackTexts.Progress.Check, check.TextContent);

        gate.Open();
        check.Click();

        page.WaitForAssertion(() => Assert.Contains("37 %, Schwäche", page.Find(".answer-progress").TextContent), Wait);
        Assert.Empty(page.FindAll(".answer-progress button"));
    }

    // AC1 and AC2 on Home: back from practice the plan carries a hint, then comes anew without any click.
    [Fact]
    public void Home_WithAPendingAnswer_ShowsTheHint_ThenTheNewPlan()
    {
        var gate = new GatedProgress(
            new TopicProgress(Topic, 50, TopicState.New, Earlier),
            new TopicProgress(Topic, 62, TopicState.Building, Attempt));
        var plans = new FakeTodayPlan()
            .Answer(Plan(new PlanBlock(Topic, 20, BlockKind.Learn), new PlanBlock(Other, 20, BlockKind.Learn)))
            .Answer(Plan(new PlanBlock(Other, 25, BlockKind.Learn), new PlanBlock(Topic, 15, BlockKind.Learn)));
        var sync = Sync(gate, Patient);
        sync.Track(Topic, Attempt);
        var page = RenderHome(plans, sync);

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Plan.Updating, page.Find("[role=status]").TextContent), Wait);
        Assert.Equal("true", page.Find(".plan-blocks").GetAttribute("aria-busy"));
        gate.Open();

        page.WaitForAssertion(() => Assert.Equal(["Zellatmung", "Photosynthese"], page.FindAll(".plan-topic").Select(name => name.TextContent)), Wait);
        Assert.DoesNotContain(FeedbackTexts.Plan.Updating, page.Find("[role=status]").TextContent);
        Assert.Equal("false", page.Find(".plan-blocks").GetAttribute("aria-busy"));
        Assert.Equal(2, plans.Calls);
        Assert.False(sync.HasPending);
    }

    // AC3 on Home: still not there after the limit, a hint and a reload, which then waits again and brings the plan.
    [Fact]
    public void Home_Late_OffersAReload()
    {
        var gate = new GatedProgress(
            new TopicProgress(Topic, 50, TopicState.New, Earlier),
            new TopicProgress(Topic, 62, TopicState.Building, Attempt));
        var plans = new FakeTodayPlan()
            .Answer(Plan(new PlanBlock(Topic, 20, BlockKind.Learn)))
            .Answer(Plan(new PlanBlock(Topic, 20, BlockKind.Learn)))
            .Answer(Plan(new PlanBlock(Other, 20, BlockKind.Learn)));
        var sync = Sync(gate);
        sync.Track(Topic, Attempt);
        var page = RenderHome(plans, sync);

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Plan.Late, page.Find("[role=status]").TextContent), Wait);
        var reload = page.Find("button");
        Assert.Equal(FeedbackTexts.Plan.Reload, reload.TextContent);

        gate.Open();
        reload.Click();

        page.WaitForAssertion(() => Assert.Equal("Zellatmung", page.Find(".plan-topic").TextContent), Wait);
        Assert.DoesNotContain(FeedbackTexts.Plan.Late, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll("button"));
    }

    // Nothing pending: Home doesn't ask LearningEngine at all.
    [Fact]
    public void Home_WithoutPendingAnswers_DoesNotWait()
    {
        var progress = new FakeLearningProgress();
        var page = RenderHome(new FakeTodayPlan().Answer(Plan(new PlanBlock(Topic, 20, BlockKind.Learn))), Sync(progress));

        Assert.Equal("Photosynthese", page.Find(".plan-topic").TextContent);
        Assert.DoesNotContain(FeedbackTexts.Plan.Updating, page.Find("[role=status]").TextContent);
        Assert.Equal(0, progress.Calls);
    }

    private static AnswerSync Sync(ILearningProgress progress, AnswerSyncTiming? timing = null) => new(progress, timing: timing ?? Fast);

    private IRenderedComponent<Practice> RenderQuiz(ILearningProgress progress, AnswerSyncTiming? timing = null)
    {
        Services.AddSingleton<IQuiz>(new FakeQuiz().Ask(Question).Answer(new GradedAnswer(Attempt, 1, true, 1, "Weil.", 3000)));
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(Topics));
        Services.AddSingleton(Sync(progress, timing));
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));
    }

    private IRenderedComponent<TodayPlanView> RenderHome(FakeTodayPlan plans, AnswerSync sync)
    {
        Services.AddSingleton<ITodayPlan>(plans);
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(Topics));
        Services.AddSingleton(sync);
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<TodayPlanView>();
    }

    private static void Answer(IRenderedComponent<Practice> page)
    {
        page.Find("#option-1").Change("1");
        page.Find("form").Submit();
    }

    private static TodayPlan Plan(params PlanBlock[] blocks) => new(new DateOnly(2026, 11, 11), blocks, null, true, [], []);

    // LearningEngine before the answer arrives, until the test opens the gate, and after.
    private sealed class GatedProgress(TopicProgress before, TopicProgress after) : ILearningProgress
    {
        private volatile bool open;

        public void Open() => open = true;

        public Task<CallOutcome<IReadOnlyList<TopicProgress>>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CallOutcome<IReadOnlyList<TopicProgress>>.Ok([open ? after : before]));
    }
}