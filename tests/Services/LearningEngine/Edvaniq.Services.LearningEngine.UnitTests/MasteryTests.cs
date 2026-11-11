using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// The thin rule: start at 50, every answer moves a quarter of the way to 100 (correct) or 0 (wrong).
public sealed class MasteryTests
{
    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutAnswers_IsTheStartValue()
    {
        Assert.Equal(Mastery.Start, Mastery.Of([]));
        Assert.Equal(50, Mastery.Percent(Mastery.Of([])));
    }

    [Fact]
    public void CorrectAnswer_RaisesIt_WrongAnswer_LowersIt()
    {
        Assert.Equal(62.5, Mastery.Of([Answer(correct: true)]));
        Assert.Equal(37.5, Mastery.Of([Answer(correct: false)]));
    }

    [Fact]
    public void EveryAnswer_MovesItAQuarterOfTheWay()
    {
        var answers = new[] { true, true, false }.Select((correct, minute) => Answer(correct, Monday.AddMinutes(minute)));

        // 50 -> 62.5 -> 71.875 -> 53.90625
        Assert.Equal(53.90625, Mastery.Of(answers));
    }

    [Fact]
    public void ManyEqualAnswers_StayWithinTheScale()
    {
        Assert.InRange(Mastery.Of(Answers(200, correct: true)), 0, 100);
        Assert.InRange(Mastery.Of(Answers(200, correct: false)), 0, 100);
        Assert.Equal(100, Mastery.Percent(Mastery.Of(Answers(200, correct: true))));
        Assert.Equal(0, Mastery.Percent(Mastery.Of(Answers(200, correct: false))));
    }

    // Newer answers weigh more: wrong then right ends above right then wrong.
    [Fact]
    public void WrongThenRight_EndsAboveRightThenWrong()
    {
        var wrongThenRight = Mastery.Of([Answer(false, Monday), Answer(true, Monday.AddMinutes(1))]);
        var rightThenWrong = Mastery.Of([Answer(true, Monday), Answer(false, Monday.AddMinutes(1))]);

        Assert.True(wrongThenRight > rightThenWrong);
    }

    // The order comes from the answers, not from when their events arrived.
    [Fact]
    public void OrderOfTheRecords_DoesNotMatter()
    {
        var first = Answer(false, Monday);
        var second = Answer(true, Monday.AddMinutes(1));
        var sameTime = new[] { Answer(true, Monday), Answer(false, Monday) };

        Assert.Equal(Mastery.Of([first, second]), Mastery.Of([second, first]));
        Assert.Equal(Mastery.Of(sameTime), Mastery.Of(sameTime.Reverse()));
    }

    // 62.5 is shown as 63, like commercial rounding.
    [Fact]
    public void Percent_RoundsHalfAwayFromZero()
    {
        Assert.Equal(63, Mastery.Percent(62.5));
        Assert.Equal(38, Mastery.Percent(37.5));
        Assert.Equal(54, Mastery.Percent(53.90625));
    }

    private static IEnumerable<LearningRecord> Answers(int count, bool correct) =>
        Enumerable.Range(0, count).Select(minute => Answer(correct, Monday.AddMinutes(minute)));

    private static LearningRecord Answer(bool correct, DateTime? answeredAt = null) =>
        LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), null, correct, 2, 4200, null, Mode.Practice, answeredAt ?? Monday);
}