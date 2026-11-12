using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #281: the overview names each goal with kind and date and leads to its material, and without a goal it leads to the
// assistant.
public sealed class GoalListTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly LearningGoalInfo Bio = new(
        Guid.NewGuid(), "ExamPreparation", "Biologie-Klausur", "Klausur", new DateOnly(2027, 3, 1), "UpperSecondary", "de", null,
        DateTime.UnixEpoch, InPlan: true);

    private static readonly LearningGoalInfo Csharp = new(
        Guid.NewGuid(), "FreeLearning", "C# lernen", null, null, null, "de", null, DateTime.UnixEpoch, InPlan: false);

    private readonly FakeLearningGoals goals = new();
    private readonly FakeReturnSignal signal = new();

    public GoalListTests()
    {
        Services.AddSingleton<ILearningGoals>(goals);
        Services.AddSingleton<IReturnSignal>(signal);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    // AC1: name, kind and date, and the way to material and topics.
    [Fact]
    public void Goals_ShowNameKindAndDate()
    {
        goals.List(Bio, Csharp);

        var page = Render<GoalList>();

        var cards = page.FindAll(".goal-card");
        Assert.Equal(2, cards.Count);
        Assert.Equal("Biologie-Klausur", cards[0].QuerySelector("h2")!.TextContent);
        Assert.Equal(FeedbackTexts.Goal.ExamPreparation, cards[0].QuerySelector(".goal-kind")!.TextContent);
        Assert.Equal("Klausur am 1. März 2027", cards[0].QuerySelector(".goal-exam")!.TextContent);
        Assert.Equal("C# lernen", cards[1].QuerySelector("h2")!.TextContent);
        Assert.Equal(FeedbackTexts.Goal.FreeLearning, cards[1].QuerySelector(".goal-kind")!.TextContent);
        Assert.Null(cards[1].QuerySelector(".goal-exam"));
        Assert.Equal("lernziele/neu", page.Find(".state-actions a").GetAttribute("href"));
    }

    // AC1: only the goal the plan follows leads to the material, which is that goal's. The others say why not.
    [Fact]
    public void OnlyTheGoalInThePlan_LeadsToTheMaterial()
    {
        goals.List(Csharp, Bio);

        var page = Render<GoalList>();

        var cards = page.FindAll(".goal-card");
        Assert.Null(cards[0].QuerySelector("a"));
        Assert.Equal(FeedbackTexts.Goal.NotFirst, cards[0].QuerySelector(".goal-not-in-plan")!.TextContent);
        var material = cards[1].QuerySelector("a.goal-material")!;
        Assert.Equal("themen", material.GetAttribute("href"));
        Assert.Equal(FeedbackTexts.GoalList.Material, material.TextContent.Trim());
        Assert.StartsWith(FeedbackTexts.GoalList.Material, material.GetAttribute("aria-label"));
        Assert.Null(cards[1].QuerySelector(".goal-not-in-plan"));
    }

    [Fact]
    public void ExamWithoutDate_SaysSo()
    {
        goals.List(Bio with { ExamType = "Ihk", ExamDate = null });

        var page = Render<GoalList>();

        Assert.Equal("IHK-Prüfung, noch ohne Termin", page.Find(".goal-exam").TextContent);
    }

    // A kind or exam type Planning adds later shows as it comes rather than breaking the list.
    [Fact]
    public void UnknownValues_ShowAsTheyCome()
    {
        goals.List(Bio with { Kind = "Somethingnew" }, Csharp with { Kind = "ExamPreparation", ExamType = "Abitur" });

        var page = Render<GoalList>();

        Assert.Equal("Somethingnew", page.FindAll(".goal-kind")[0].TextContent);
        Assert.Equal("Abitur, noch ohne Termin", page.Find(".goal-exam").TextContent);
    }

    // AC2: without a goal a hint says so and a button leads to the assistant.
    [Fact]
    public void WithoutGoals_LeadsToTheAssistant()
    {
        var page = Render<GoalList>();

        var empty = page.Find("[role=status] .empty-state");
        Assert.Contains(FeedbackTexts.GoalList.Empty, empty.TextContent);
        var create = page.Find(".state-actions a.button");
        Assert.Equal(FeedbackTexts.GoalList.Create, create.TextContent);
        Assert.Equal("lernziele/neu", create.GetAttribute("href"));
        Assert.Empty(page.FindAll(".goal-card"));
    }

    [Fact]
    public void Failure_ShowsTheProblem_AndRetryLoadsAgain()
    {
        goals.List(CallOutcome<IReadOnlyList<LearningGoalInfo>>.Failed(TraceId)).List(Bio);

        var page = Render<GoalList>();

        var problem = page.Find("[role=status] .connection-problem");
        Assert.Contains(FeedbackTexts.GoalList.LoadFailed, problem.TextContent);
        Assert.Contains(TraceId, problem.TextContent);

        page.Find(".state-actions button").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".goal-card")));
    }

    // The shell says the login ran out (#197), the page adds nothing of its own.
    [Fact]
    public void SignedOut_ShowsNothingOfItsOwn()
    {
        goals.List(CallOutcome<IReadOnlyList<LearningGoalInfo>>.SignedOut);

        var page = Render<GoalList>();

        Assert.Equal("", page.Find("[role=status]").TextContent.Trim());
        Assert.Empty(page.FindAll(".state-actions"));
    }

    // Back on the page, a goal made on another device shows, without a loading hint in between.
    [Fact]
    public void Return_ShowsNewGoals()
    {
        goals.List(Bio).List(Bio, Csharp);
        var page = Render<GoalList>();
        Assert.Single(page.FindAll(".goal-card"));

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".goal-card").Count));
    }
}