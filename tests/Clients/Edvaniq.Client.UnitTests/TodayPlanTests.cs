using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #212: Home shows today's plan as a list, says what to do next when it's empty, and shows the new plan after an answer.
public sealed class TodayPlanTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly Guid Photosynthesis = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid LightReaction = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    private static readonly Guid Respiration = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");

    private static readonly TopicInfo[] Topics =
    [
        new(Photosynthesis, "Photosynthese", [new SubtopicInfo(LightReaction, "Lichtreaktion")]),
        new(Respiration, "Zellatmung", []),
    ];

    private readonly FakeReturnSignal signal = new();

    public TodayPlanTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<IReturnSignal>(signal);
    }

    [Fact]
    public void Blocks_ShowName_KindAndMinutes_AndLeadToPractice()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(
            new PlanBlock(Respiration, 15, BlockKind.Review),
            new PlanBlock(LightReaction, 20, BlockKind.Learn),
            new PlanBlock(Photosynthesis, 1, BlockKind.Learn))));

        var blocks = page.FindAll(".plan-block");
        Assert.Equal(3, blocks.Count);
        Assert.Equal("Zellatmung", blocks[0].QuerySelector(".plan-topic")!.TextContent);
        Assert.Equal(FeedbackTexts.Plan.Review, blocks[0].QuerySelector(".plan-kind")!.TextContent);
        Assert.Equal("15 Minuten", blocks[0].QuerySelector(".plan-minutes")!.TextContent);
        Assert.Equal("Lichtreaktion", blocks[1].QuerySelector(".plan-topic")!.TextContent);
        Assert.Equal(FeedbackTexts.Plan.Learn, blocks[1].QuerySelector(".plan-kind")!.TextContent);
        Assert.Equal("1 Minute", blocks[2].QuerySelector(".plan-minutes")!.TextContent);
        Assert.Equal($"themen/{Respiration:D}/ueben", blocks[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("„Zellatmung“", blocks[0].QuerySelector("a")!.TextContent);
        Assert.Empty(page.FindAll(".plan-hint"));
    }

    // A kind Planning added later shows no kind rather than a wrong one, and a topic Knowledge doesn't name stays in.
    [Fact]
    public void UnknownKindAndTopic_StayInTheList_WithoutGuessing()
    {
        var unknown = Guid.NewGuid();
        var page = Render(new FakeTodayPlan().Answer(Plan(new PlanBlock(unknown, 10, Kind: null))));

        var block = page.Find(".plan-block");
        Assert.Equal(FeedbackTexts.Plan.UnnamedTopic, block.QuerySelector(".plan-topic")!.TextContent);
        Assert.Null(block.QuerySelector(".plan-kind"));
        Assert.Equal("10 Minuten", block.QuerySelector(".plan-minutes")!.TextContent);
    }

    // AC1: back from practice the plan is asked anew and shows the new order, without a loading hint in between.
    [Fact]
    public void AfterAnAnswer_TheReturnShowsTheNewPlan()
    {
        var plans = new FakeTodayPlan()
            .Answer(Plan(new PlanBlock(Photosynthesis, 20, BlockKind.Learn), new PlanBlock(Respiration, 20, BlockKind.Learn)))
            .Answer(Plan(new PlanBlock(Respiration, 25, BlockKind.Learn), new PlanBlock(Photosynthesis, 15, BlockKind.Learn)));
        var page = Render(plans);
        Assert.Equal(["Photosynthese", "Zellatmung"], page.FindAll(".plan-topic").Select(name => name.TextContent));

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal(["Zellatmung", "Photosynthese"], page.FindAll(".plan-topic").Select(name => name.TextContent)));
        Assert.Equal(["25 Minuten", "15 Minuten"], page.FindAll(".plan-minutes").Select(minutes => minutes.TextContent));
        Assert.Equal(2, plans.Calls);
    }

    // AC1: opening Home again, the way back from the practice page, asks anew too.
    [Fact]
    public async Task EachVisit_AsksAnew()
    {
        var plans = new FakeTodayPlan()
            .Answer(Plan(new PlanBlock(Photosynthesis, 20, BlockKind.Learn)))
            .Answer(Plan(new PlanBlock(Respiration, 20, BlockKind.Review)));
        Render(plans);
        await DisposeComponentsAsync();

        var page = Render<TodayPlanView>();

        Assert.Equal("Zellatmung", page.Find(".plan-topic").TextContent);
        Assert.Equal(2, plans.Calls);
    }

    // AC2: each reason for an empty plan names the next step and leads there with one click.
    [Theory]
    [InlineData(PlanEmpty.NoTopics, FeedbackTexts.Plan.NoTopics, FeedbackTexts.Plan.NoTopicsLink)]
    [InlineData(PlanEmpty.QuestionsPending, FeedbackTexts.Plan.QuestionsPending, FeedbackTexts.Plan.QuestionsPendingLink)]
    [InlineData(PlanEmpty.AllMastered, FeedbackTexts.Plan.AllMastered, FeedbackTexts.Plan.AllMasteredLink)]
    [InlineData(null, FeedbackTexts.Plan.Nothing, FeedbackTexts.Plan.NothingLink)]
    public void EmptyPlan_ShowsTheNextStep(PlanEmpty? reason, string title, string link)
    {
        var page = Render(new FakeTodayPlan().Answer(Plan() with { Empty = reason }));

        var status = page.Find("[role=status]");
        Assert.Contains(title, status.TextContent);
        var next = status.QuerySelector("a")!;
        Assert.Equal(link, next.TextContent);
        Assert.Equal("themen", next.GetAttribute("href"));
        Assert.Empty(page.FindAll(".plan-block"));
    }

    // Questions on their way: the empty plan already says it, no second hint with the same topics.
    [Fact]
    public void QuestionsPending_HasNoSecondHint()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan() with { Empty = PlanEmpty.QuestionsPending, NotReadyTopicIds = [Photosynthesis] }));

        Assert.Empty(page.FindAll(".plan-not-ready"));
    }

    [Fact]
    public void WithoutMastery_SaysTheOrderIsProvisional()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(new PlanBlock(Photosynthesis, 20, BlockKind.Learn)) with { MasteryAvailable = false }));

        Assert.Contains(FeedbackTexts.Plan.MasteryUnavailable, page.Find(".plan-mastery").TextContent);
        Assert.Single(page.FindAll(".plan-block"));
    }

    [Fact]
    public void ShortenedAndNotReadyTopics_AreNamed()
    {
        var page = Render(new FakeTodayPlan().Answer(Plan(new PlanBlock(Photosynthesis, 20, BlockKind.Learn)) with
        {
            ShortenedTopicIds = [Respiration],
            NotReadyTopicIds = [LightReaction],
        }));

        var shortened = page.Find(".plan-shortened");
        Assert.Contains(FeedbackTexts.Plan.Shortened, shortened.TextContent);
        Assert.Equal(["Zellatmung"], shortened.QuerySelectorAll("li").Select(item => item.TextContent));
        var notReady = page.Find(".plan-not-ready");
        Assert.Contains(FeedbackTexts.Plan.NotReady, notReady.TextContent);
        Assert.Equal(["Lichtreaktion"], notReady.QuerySelectorAll("li").Select(item => item.TextContent));
        Assert.Empty(page.FindAll(".plan-mastery"));
    }

    // A failed call shows the trace and a retry, which then brings the plan.
    [Fact]
    public void Failure_ShowsTheProblem_AndRetryBringsThePlan()
    {
        var plans = new FakeTodayPlan()
            .Answer(CallOutcome<TodayPlan>.Failed(TraceId))
            .Answer(Plan(new PlanBlock(Photosynthesis, 20, BlockKind.Learn)));
        var page = Render(plans);
        Assert.Contains(FeedbackTexts.Plan.LoadFailed, page.Find("[role=status]").TextContent);
        Assert.Contains(TraceId, page.Find("[role=status]").TextContent);

        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Equal("Photosynthese", page.Find(".plan-topic").TextContent));
    }

    // Without the names the list would be no use, so a failed topic call counts as a failed load.
    [Fact]
    public void TopicsFailing_IsAFailedLoad()
    {
        var page = Render(
            new FakeTodayPlan().Answer(Plan(new PlanBlock(Photosynthesis, 20, BlockKind.Learn))),
            new FakeLearningMaterial().Topics(CallOutcome<IReadOnlyList<TopicInfo>>.Failed()));

        Assert.Contains(FeedbackTexts.Plan.LoadFailed, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll(".plan-block"));
    }

    // The shell says the login ran out (#197), the plan adds nothing of its own: no hint, no error, no link.
    [Fact]
    public void SignedOut_ShowsNothingOfItsOwn()
    {
        var page = Render(new FakeTodayPlan().Answer(CallOutcome<TodayPlan>.SignedOut));

        Assert.Equal("", page.Find("[role=status]").TextContent.Trim());
        Assert.Empty(page.FindAll(".plan-block"));
        Assert.Empty(page.FindAll("a"));
        Assert.Empty(page.FindAll("button"));
    }

    // A plan already shown stays when the login runs out on a return.
    [Fact]
    public void SignedOut_OnReturn_KeepsThePlan()
    {
        var plans = new FakeTodayPlan()
            .Answer(Plan(new PlanBlock(Photosynthesis, 20, BlockKind.Learn)))
            .Answer(CallOutcome<TodayPlan>.SignedOut);
        var page = Render(plans);

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal(2, plans.Calls));
        Assert.Equal("Photosynthese", page.Find(".plan-topic").TextContent);
        Assert.DoesNotContain(FeedbackTexts.Plan.LoadFailed, page.Find("[role=status]").TextContent);
    }

    private IRenderedComponent<TodayPlanView> Render(FakeTodayPlan plans, FakeLearningMaterial? material = null)
    {
        Services.AddSingleton<ITodayPlan>(plans);
        Services.AddSingleton<ILearningMaterial>(material ?? new FakeLearningMaterial().Topics(Topics));
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<TodayPlanView>();
    }

    private static TodayPlan Plan(params PlanBlock[] blocks) => new(Today, blocks, Empty: null, MasteryAvailable: true, [], []);
}