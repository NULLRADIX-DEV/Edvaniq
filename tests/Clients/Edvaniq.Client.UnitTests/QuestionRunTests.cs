using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Pages;

namespace Edvaniq.Client.UnitTests;

// #358: where a run of questions stands, shared by the block quiz and the new topic unit. The views are covered by their
// own tests, these hold the counting.
public sealed class QuestionRunTests
{
    private static readonly AskedQuestion First = Question("Erste");
    private static readonly AskedQuestion Second = Question("Zweite");
    private static readonly AskedQuestion Third = Question("Dritte");

    [Fact]
    public void FreshQuestions_OneAfterAnother_CountWhatWasRight()
    {
        var run = new QuestionRun([new(First), new(Second), new(Third)]);

        Assert.Same(First, run.Current);
        run.Grade(true);
        run.MoveOn();
        Assert.Same(Second, run.Current);
        run.Grade(false);
        run.MoveOn();
        Assert.True(run.IsLast);
        run.MoveOn();

        Assert.True(run.IsDone);
        Assert.Equal(new QuizTally(1, 2, 3), run.Tally);
    }

    // The card starts afresh when it gets another instance, so the current question must stay the same object.
    [Fact]
    public void Current_IsTheSameInstance_EachTime()
    {
        var run = new QuestionRun([new(First with { })]);

        Assert.Same(run.Current, run.Current);
    }

    [Fact]
    public void AnsweredBefore_Counts_AndIsSkipped()
    {
        var run = new QuestionRun([new(First, true, true), new(Second), new(Third, true, false)]);

        Assert.Equal(1, run.Index);
        Assert.True(run.IsLast);
        run.MoveOn();

        Assert.True(run.IsDone);
        Assert.Equal(new QuizTally(1, 2, 3), run.Tally);
    }

    [Fact]
    public void AllAnsweredBefore_IsDoneAtOnce()
    {
        var run = new QuestionRun([new(First, true, true)]);

        Assert.True(run.IsDone);
        Assert.Equal(new QuizTally(1, 1, 1), run.Tally);
    }

    private static AskedQuestion Question(string text) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), text, ["Ja", "Nein"], 1, QuestionTypes.SingleChoice);
}