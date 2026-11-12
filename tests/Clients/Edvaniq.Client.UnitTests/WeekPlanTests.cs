using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #379: /plan lists the days from today on, each with its blocks or a note that nothing is planned, marks shortened
// topics in words and leads to a new learning goal while there are no topics.
public sealed class WeekPlanTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    // Wednesday.
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly Guid Photosynthesis = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Respiration = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");

    private static readonly TopicInfo[] Topics =
    [
        new(Photosynthesis, "Photosynthese", []),
        new(Respiration, "Zellatmung", []),
    ];

    private readonly FakeReturnSignal signal = new();

    public WeekPlanTests() => Services.AddSingleton<IReturnSignal>(signal);

    [Fact]
    public void Days_ShowDate_BlocksOrNothing()
    {
        var page = Render(new FakeWeekPlan().Answer(Week(
            [new PlanBlock(Respiration, 5, BlockKind.Review), new PlanBlock(Photosynthesis, 25, BlockKind.Learn)],
            [],
            [new PlanBlock(Photosynthesis, 1, BlockKind.Learn)])));

        var days = page.FindAll(".week-day");
        Assert.Equal(3, days.Count);
        Assert.Equal(
            ["Heute, Mittwoch, 11. November", "Donnerstag, 12. November", "Freitag, 13. November"],
            page.FindAll(".week-date").Select(date => date.TextContent));
        Assert.Equal(["Zellatmung", "Photosynthese"], days[0].QuerySelectorAll(".week-topic").Select(name => name.TextContent));
        Assert.Equal(FeedbackTexts.Plan.Review, days[0].QuerySelector(".week-kind")!.TextContent);
        Assert.Equal("5 Minuten", days[0].QuerySelector(".week-minutes")!.TextContent);
        Assert.Equal(FeedbackTexts.Week.FreeDay, days[1].QuerySelector(".week-free")!.TextContent);
        Assert.Null(days[1].QuerySelector(".week-blocks"));
        Assert.Equal("1 Minute", days[2].QuerySelector(".week-minutes")!.TextContent);
        Assert.Empty(page.FindAll(".plan-hint"));
        Assert.Empty(page.FindAll(".week-shortened"));
    }

    // Each day's list is named by its heading, so a screen reader says which day a block belongs to.
    [Fact]
    public void EachList_IsNamedByItsDay()
    {
        var page = Render(new FakeWeekPlan().Answer(Week([new PlanBlock(Photosynthesis, 20, BlockKind.Learn)])));

        var heading = page.Find(".week-date");
        Assert.Equal(heading.Id, page.Find(".week-blocks").GetAttribute("aria-labelledby"));
    }

    // AC: shortened topics are marked in words at their blocks and named in the hint below.
    [Fact]
    public void ShortenedTopics_AreMarkedInWords()
    {
        var page = Render(new FakeWeekPlan().Answer(Week(
            [new PlanBlock(Respiration, 5, BlockKind.Review), new PlanBlock(Photosynthesis, 20, BlockKind.Learn)]) with
        {
            ShortenedTopicIds = [Respiration],
        }));

        var blocks = page.FindAll(".week-block");
        Assert.Equal(FeedbackTexts.Week.ShortenedMark, blocks[0].QuerySelector(".week-shortened")!.TextContent);
        Assert.Null(blocks[1].QuerySelector(".week-shortened"));
        Assert.Contains(FeedbackTexts.Plan.Shortened, page.Find(".plan-shortened").TextContent);
        Assert.Equal(["Zellatmung"], page.FindAll(".plan-shortened li").Select(item => item.TextContent));
    }

    // AC: without topics the next step is a new learning goal.
    [Fact]
    public void WithoutTopics_LeadsToANewLearningGoal()
    {
        var page = Render(new FakeWeekPlan().Answer(Week([], []) with { Empty = PlanEmpty.NoTopics }));

        Assert.Contains(FeedbackTexts.Week.NoTopics, page.Find("[role=status]").TextContent);
        var link = page.Find(".state-actions a");
        Assert.Equal("lernziele/neu", link.GetAttribute("href"));
        Assert.Equal(FeedbackTexts.Week.NoTopicsLink, link.TextContent);
        Assert.Empty(page.FindAll(".week-day"));
    }

    [Theory]
    [InlineData(PlanEmpty.QuestionsPending, FeedbackTexts.Plan.QuestionsPending)]
    [InlineData(PlanEmpty.AllMastered, FeedbackTexts.Week.AllMastered)]
    [InlineData(null, FeedbackTexts.Week.Nothing)]
    public void OtherEmptyWeeks_LeadToTheTopics(PlanEmpty? reason, string title)
    {
        var page = Render(new FakeWeekPlan().Answer(Week([]) with { Empty = reason }));

        Assert.Contains(title, page.Find("[role=status]").TextContent);
        Assert.Equal("themen", page.Find(".state-actions a").GetAttribute("href"));
    }

    [Fact]
    public void LoadFailed_ShowsTheTrace_AndRetries()
    {
        var plans = new FakeWeekPlan()
            .Answer(CallOutcome<WeekPlan>.Failed(TraceId))
            .Answer(Week([new PlanBlock(Photosynthesis, 20, BlockKind.Learn)]));
        var page = Render(plans);
        Assert.Contains(FeedbackTexts.Week.LoadFailed, page.Find("[role=status]").TextContent);
        Assert.Contains(TraceId, page.Find("[role=status]").TextContent);

        page.Find(".state-actions button").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".week-block")));
    }

    [Fact]
    public void Return_AsksAgain()
    {
        var plans = new FakeWeekPlan()
            .Answer(Week([new PlanBlock(Photosynthesis, 20, BlockKind.Learn)]))
            .Answer(Week([new PlanBlock(Respiration, 20, BlockKind.Learn)]));
        var page = Render(plans);

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal("Zellatmung", page.Find(".week-topic").TextContent));
    }

    private IRenderedComponent<WeekPlanView> Render(FakeWeekPlan plans)
    {
        Services.AddSingleton<IWeekPlan>(plans);
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(Topics));
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<WeekPlanView>();
    }

    private static WeekPlan Week(params PlanBlock[][] days) =>
        new([.. days.Select((blocks, offset) => new PlanDay(Today.AddDays(offset), blocks))], Empty: null, MasteryAvailable: true, [], []);
}