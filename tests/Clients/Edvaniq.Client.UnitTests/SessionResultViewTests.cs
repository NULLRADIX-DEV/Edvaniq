using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #406: the result of a session never shows a mastery that isn't complete. While LearningEngine catches up the counts
// show with a loading state, past the limit a hint with a button to check again.
public sealed class SessionResultViewTests : BunitContext
{
    private static readonly Guid Session = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");

    // AC1: complete, the counts and the mastery before and after.
    [Fact]
    public void Complete_ShowsCountsAndTheChange()
    {
        var page = Render(new FakeSessionResults().Round(CallOutcome<SessionReport>.Ok(Report(complete: true))));

        Assert.Equal("3 von 5 richtig", page.Find(".session-result-counts").TextContent);
        Assert.Equal("50 % → 62 %", page.Find(".session-result-topic").TextContent);
        Assert.Equal(FeedbackTexts.SessionReport.NextBlock, page.Find(".session-result-next").TextContent);
        Assert.Empty(page.FindAll(".state-actions"));
    }

    // AC2: while waiting the counts show, the mastery is a loading state, never a number.
    [Fact]
    public void Waiting_ShowsCountsAndLoading_NoMastery()
    {
        var gate = new TaskCompletionSource();
        var results = new FakeSessionResults { Gate = gate }
            .Round(CallOutcome<SessionReport>.Ok(Report(complete: true)), Report(complete: false));
        var page = Render(results);

        page.WaitForAssertion(() => Assert.Equal("3 von 5 richtig", page.Find(".session-result-counts").TextContent));
        Assert.Contains(FeedbackTexts.SessionReport.Evaluating, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll(".session-result-topic"));

        gate.SetResult();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".session-result-topic")));
    }

    // AC2: past the limit a plain hint and a button. Checking again waits anew and shows the result once it's there.
    [Fact]
    public async Task PastTheLimit_HintAndCheckAgain()
    {
        var results = new FakeSessionResults()
            .Round(CallOutcome<SessionReport>.Ok(Report(complete: false)))
            .Round(CallOutcome<SessionReport>.Ok(Report(complete: true)));
        var page = Render(results);

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.SessionReport.Slow, page.Find(".session-result-slow").TextContent));
        Assert.Empty(page.FindAll(".session-result-topic"));
        Assert.Equal("3 von 5 richtig", page.Find(".session-result-counts").TextContent);

        await page.Find(".state-actions button").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".session-result-topic")));
        Assert.Equal(2, results.Waits);
        Assert.Empty(page.FindAll(".session-result-slow"));
    }

    [Fact]
    public void TopicRemoved_SaysSo_WithoutMastery()
    {
        var page = Render(new FakeSessionResults().Round(CallOutcome<SessionReport>.Ok(Report(complete: false) with { TopicRemoved = true })));

        Assert.Contains(FeedbackTexts.SessionReport.TopicRemoved, page.Find(".session-result-removed").TextContent);
        Assert.Empty(page.FindAll(".session-result-topic"));
        Assert.Empty(page.FindAll(".state-actions"));
    }

    [Fact]
    public async Task Failure_ShowsTheConnectionProblem_AndRetries()
    {
        var results = new FakeSessionResults()
            .Round(CallOutcome<SessionReport>.Failed("4bf92f3577b34da6a3ce929d0e0e4736"))
            .Round(CallOutcome<SessionReport>.Ok(Report(complete: true)));
        var page = Render(results);

        page.WaitForAssertion(() => Assert.Contains(FeedbackTexts.SessionReport.LoadFailed, page.Markup));
        Assert.Empty(page.FindAll(".session-result-counts"));

        await page.Find(".state-actions button").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".session-result-topic")));
    }

    [Fact]
    public void LastBlock_OffersABonusReview()
    {
        var page = Render(new FakeSessionResults().Round(CallOutcome<SessionReport>.Ok(Report(complete: true) with { Next = null })));

        Assert.Equal(FeedbackTexts.SessionReport.BonusReview, page.Find(".session-result-next").TextContent);
    }

    private IRenderedComponent<SessionResultView> Render(FakeSessionResults results)
    {
        Services.AddSingleton<ISessionResults>(results);
        return Render<SessionResultView>(parameters => parameters.Add(p => p.SessionId, Session));
    }

    private static SessionReport Report(bool complete) => new(
        Session,
        Completed: true,
        Answered: 5,
        Correct: 3,
        Wrong: 2,
        Complete: complete,
        MasteryAvailable: true,
        Topics: complete ? [new MasteryChange(Topic, new(50, "new"), new(62, "building"))] : null,
        DailyGoal: DailyGoalState.Open,
        Next: new NextBlock(Guid.NewGuid(), Topic, BlockKind.Learn, 15),
        TopicRemoved: false);
}