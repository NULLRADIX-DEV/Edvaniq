using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #383: the ring shows how much of today is done and says so in words, a reached goal is confirmed once. #385: Home
// names the next step without a goal, while material is analysed and with nothing open.
public sealed class TodayProgressTests : BunitContext
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly Guid Photosynthesis = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Respiration = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");
    private static readonly Guid Fermentation = Guid.Parse("8c3d4e5f-6a7b-4c8d-9e0f-1a2b3c4d5e6f");

    private static readonly TopicInfo[] Topics =
    [
        new(Photosynthesis, "Photosynthese", []),
        new(Respiration, "Zellatmung", []),
        new(Fermentation, "Gärung", []),
    ];

    private readonly FakeReturnSignal signal = new();

    public TodayProgressTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<IReturnSignal>(signal);
    }

    // AC1 and AC3: the share of done blocks as a ring and as text, the ring itself is hidden from screen readers.
    [Theory]
    [InlineData(0, "0 von 3 Blöcken erledigt", "0 100")]
    [InlineData(1, "1 von 3 Blöcken erledigt", "33 100")]
    [InlineData(3, "3 von 3 Blöcken erledigt", "100 100")]
    public void Ring_ShowsTheDoneBlocks(int done, string text, string dash)
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(done)));

        Assert.Equal(text, page.Find(".progress-text").TextContent);
        Assert.Equal(dash, page.Find(".progress-fill").GetAttribute("stroke-dasharray"));
        Assert.Equal("true", page.Find(".progress-ring").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Ring_OfOneBlock_SaysBlock()
    {
        var page = Render(new FakeTodayPlan().Answer(new TodayPlan(
            Today, [new PlanBlock(Photosynthesis, 10, BlockKind.Learn)], null, true, [], [], DailyGoal: DailyGoal.Open)));

        Assert.Equal("0 von 1 Block erledigt", page.Find(".progress-text").TextContent);
    }

    // AC1: back from a block the ring has risen.
    [Fact]
    public void Ring_RisesAfterEachBlock()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(1)).Answer(Plan(2)));
        Assert.Equal("1 von 3 Blöcken erledigt", page.Find(".progress-text").TextContent);

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal("2 von 3 Blöcken erledigt", page.Find(".progress-text").TextContent));
        Assert.Equal("66 100", page.Find(".progress-fill").GetAttribute("stroke-dasharray"));
    }

    [Fact]
    public void Ring_NotOnAnEmptyDay()
    {
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.AllMastered)));

        Assert.Empty(page.FindAll(".progress"));
    }

    // A done block says so in words, not only in colour.
    [Fact]
    public void DoneBlock_IsMarked()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(1)));

        var blocks = page.FindAll(".plan-block");
        Assert.Equal(FeedbackTexts.Plan.Done, blocks[0].QuerySelector(".plan-done")!.TextContent);
        Assert.Null(blocks[1].QuerySelector(".plan-done"));
        Assert.Null(blocks[2].QuerySelector(".plan-done"));
    }

    // AC2 and #385 AC3: a reached goal is confirmed, with the bonus review as the next possible step.
    [Fact]
    public void ReachedGoal_IsConfirmed_WithTheBonus()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(3)));

        var confirmation = page.Find(".plan-goal-reached");
        Assert.Contains(FeedbackTexts.Plan.GoalReached, confirmation.TextContent);
        Assert.Contains(FeedbackTexts.Plan.Bonus, confirmation.TextContent);
        Assert.Contains(FeedbackTexts.Plan.GoalReached, page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void OpenGoal_HasNoConfirmation()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(2)));

        Assert.Empty(page.FindAll(".plan-goal-reached"));
        Assert.DoesNotContain(FeedbackTexts.Plan.GoalReached, page.Find("[role=status]").TextContent);
    }

    // The confirmation is said once: a return with the goal still reached leaves it out of the live region.
    [Fact]
    public void ReachedGoal_IsAnnouncedOnce()
    {
        var plans = new FakeTodayPlan().Answer(Plan(3));
        var page = Render(plans);
        Assert.Contains(FeedbackTexts.Plan.GoalReached, page.Find("[role=status]").TextContent);

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal(2, plans.Calls));
        page.WaitForAssertion(() => Assert.DoesNotContain(FeedbackTexts.Plan.GoalReached, page.Find("[role=status]").TextContent));
        Assert.Contains(FeedbackTexts.Plan.GoalReached, page.Find(".plan-goal-reached").TextContent);
    }

    // Reaching the goal while Home is open is said when it comes.
    [Fact]
    public void ReachedOnReturn_IsAnnounced()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(2)).Answer(Plan(3)));
        Assert.DoesNotContain(FeedbackTexts.Plan.GoalReached, page.Find("[role=status]").TextContent);

        signal.Return();

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Plan.GoalReached, page.Find("[role=status]").TextContent));
    }

    // #385 AC3: nothing open because the topics sit, the bonus review is possible there too.
    [Fact]
    public void AllMastered_NamesTheBonus()
    {
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.AllMastered)));

        Assert.Contains(FeedbackTexts.Plan.Bonus, page.Find("[role=status] .empty-state").TextContent);
    }

    // #385 AC1: without a goal Home leads into the wizard, the link below the live region.
    [Fact]
    public void NoGoal_LeadsIntoTheWizard()
    {
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.NoTopics)), goals: new FakeLearningGoals());

        var status = page.Find("[role=status]");
        Assert.Contains(FeedbackTexts.Plan.NoGoal, status.QuerySelector(".empty-state")!.TextContent);
        Assert.Null(status.QuerySelector("a"));
        var next = page.Find(".state-actions a.button");
        Assert.Equal(FeedbackTexts.Plan.NoGoalLink, next.TextContent);
        Assert.Equal("lernziele/neu", next.GetAttribute("href"));
    }

    // With a goal the hint stays the one from before, and with blocks the goals aren't asked at all.
    [Fact]
    public void WithAGoal_TheNoTopicsHintStays()
    {
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.NoTopics)));

        Assert.Contains(FeedbackTexts.Plan.NoTopics, page.Find("[role=status] .empty-state").TextContent);
        Assert.Equal("themen", page.Find(".state-actions a").GetAttribute("href"));
    }

    [Fact]
    public void WithBlocks_TheGoalsArentAsked()
    {
        var goals = new FakeLearningGoals();
        var page = Render(new FakeTodayPlan().Answer(Plan(0)), goals: goals);

        Assert.Equal(3, page.FindAll(".plan-block").Count);
        Assert.Equal(0, goals.ListCalls);
    }

    // A failing call for the goals leaves the hint as it was, Home doesn't fail for it.
    [Fact]
    public void GoalsFailing_KeepsTheNoTopicsHint()
    {
        var page = Render(
            new FakeTodayPlan().Answer(Empty(PlanEmpty.NoTopics)),
            goals: new FakeLearningGoals().List(CallOutcome<IReadOnlyList<LearningGoalInfo>>.Failed()));

        Assert.Contains(FeedbackTexts.Plan.NoTopics, page.Find("[role=status] .empty-state").TextContent);
        Assert.DoesNotContain(FeedbackTexts.Plan.LoadFailed, page.Find("[role=status]").TextContent);
    }

    // #385 AC2: material in the analysis is why there is no plan yet. The title is the user's, written as text.
    [Fact]
    public void Analysis_IsTheStatusOfAnEmptyPlan()
    {
        var material = Material().Documents(
            Document("Altes Skript", DocumentStatus.Ready, 1),
            Document("Skript <b>Bio</b>", DocumentStatus.Analyzing, 2));
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.NoTopics)), material);

        var state = page.Find("[role=status] .empty-state");
        Assert.Contains("„Skript <b>Bio</b>“ wird gerade analysiert.", state.TextContent);
        Assert.Empty(state.QuerySelectorAll("b"));
        Assert.Equal("themen", page.Find(".state-actions a").GetAttribute("href"));
        Assert.Equal(FeedbackTexts.Plan.AnalyzingLink, page.Find(".state-actions a").TextContent);
    }

    // A file still being read counts as in the analysis too.
    [Fact]
    public void UploadedFile_CountsAsAnalysis()
    {
        var material = Material().Documents(Document("Skript.pdf", DocumentStatus.Uploaded, 1));
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.NoTopics)), material);

        Assert.Contains("„Skript.pdf“ wird gerade analysiert.", page.Find("[role=status]").TextContent);
    }

    // Without a goal that comes first, the analysis can wait.
    [Fact]
    public void NoGoal_ComesBeforeTheAnalysis()
    {
        var material = Material().Documents(Document("Skript", DocumentStatus.Analyzing, 1));
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.NoTopics)), material, new FakeLearningGoals());

        Assert.Contains(FeedbackTexts.Plan.NoGoal, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll(".plan-analyzing"));
    }

    // With blocks the plan doesn't wait for the documents, the status follows once they are there.
    [Fact]
    public void Analysis_WithBlocks_FollowsThePlan()
    {
        var gate = new TaskCompletionSource();
        var material = Material().Documents(Document("Neues Skript", DocumentStatus.Analyzing, 1));
        material.ListGate = gate;
        var page = Render(new FakeTodayPlan().Answer(Plan(0)), material);
        Assert.Equal(3, page.FindAll(".plan-block").Count);
        Assert.Empty(page.FindAll(".plan-analyzing"));

        gate.SetResult();

        page.WaitForAssertion(() => Assert.Contains("„Neues Skript“ wird gerade analysiert.", page.Find("[role=status] .plan-analyzing").TextContent));
        Assert.Equal("themen", page.Find(".plan-analyzing a").GetAttribute("href"));
    }

    [Fact]
    public void DocumentsFailing_ShowThePlanWithoutAStatus()
    {
        var material = Material().Documents(CallOutcome<IReadOnlyList<DocumentInfo>>.Failed());
        var page = Render(new FakeTodayPlan().Answer(Plan(0)), material);

        Assert.Equal(3, page.FindAll(".plan-block").Count);
        Assert.Empty(page.FindAll(".plan-analyzing"));
        Assert.DoesNotContain(FeedbackTexts.Plan.LoadFailed, page.Find("[role=status]").TextContent);
    }

    [Fact]
    public void ReadyDocuments_HaveNoStatus()
    {
        var material = Material().Documents(Document("Skript", DocumentStatus.Ready, 1), Document("Kaputt", DocumentStatus.Failed, 2));
        var page = Render(new FakeTodayPlan().Answer(Empty(PlanEmpty.NoTopics)), material);

        Assert.Contains(FeedbackTexts.Plan.NoTopics, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll(".plan-analyzing"));
    }

    private IRenderedComponent<TodayPlanView> Render(FakeTodayPlan plans, FakeLearningMaterial? material = null, FakeLearningGoals? goals = null)
    {
        Services.AddSingleton<ITodayPlan>(plans);
        Services.AddSingleton<ILearningMaterial>(material ?? Material());
        Services.AddSingleton<ILearningGoals>(goals ?? FakeLearningGoals.WithOne());
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<TodayPlanView>();
    }

    private static FakeLearningMaterial Material() => new FakeLearningMaterial().Topics(Topics);

    // Three blocks, the first `done` of them finished.
    private static TodayPlan Plan(int done) => new(
        Today,
        [
            new PlanBlock(Photosynthesis, 10, BlockKind.Review, Done: done > 0),
            new PlanBlock(Respiration, 20, BlockKind.Learn, Done: done > 1),
            new PlanBlock(Fermentation, 15, BlockKind.Learn, Done: done > 2),
        ],
        Empty: null,
        MasteryAvailable: true,
        [],
        [],
        DailyGoal: done == 3 ? DailyGoal.Reached : DailyGoal.Open);

    private static TodayPlan Empty(PlanEmpty reason) => new(Today, [], reason, MasteryAvailable: true, [], []);

    private static DocumentInfo Document(string title, DocumentStatus status, int minute) =>
        new(Guid.NewGuid(), title, status, new DateTimeOffset(2026, 11, 11, 9, minute, 0, TimeSpan.Zero));
}