using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// Rule version 2: start at 50, an answer moves 0.15 / 0.25 / 0.35 of the way to 100 or 0 by difficulty 1 / 2 / 3,
// and the value halves every 90 days without answers. The rows below are worked out by hand.
public sealed class MasteryTests
{
    private const int Precision = 9;

    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutAnswers_IsTheStartValue_AndDoesNotDecay()
    {
        Assert.Equal(Mastery.Start, Mastery.Of([], Monday));
        Assert.Equal(Mastery.Start, Mastery.Of([], Monday.AddYears(1)));
        Assert.Equal(50, Mastery.Percent(Mastery.Of([], Monday)));
    }

    [Fact]
    public void CorrectAnswer_RaisesIt_WrongAnswer_LowersIt()
    {
        Assert.Equal(62.5, Mastery.Of([Answer(correct: true)], Monday), Precision);
        Assert.Equal(37.5, Mastery.Of([Answer(correct: false)], Monday), Precision);
    }

    [Fact]
    public void HarderQuestions_MoveItFurther()
    {
        Assert.Equal(57.5, Mastery.Of([Answer(true, difficulty: 1)], Monday), Precision);
        Assert.Equal(67.5, Mastery.Of([Answer(true, difficulty: 3)], Monday), Precision);
        Assert.Equal(42.5, Mastery.Of([Answer(false, difficulty: 1)], Monday), Precision);
        Assert.Equal(32.5, Mastery.Of([Answer(false, difficulty: 3)], Monday), Precision);
    }

    // No difficulty counts as the middle one, anything outside 1 to 3 as the nearest end.
    [Fact]
    public void MissingOrOddDifficulty_CountsAsTheNearestKnownOne()
    {
        Assert.Equal(Mastery.MediumStep, Mastery.StepFor(null));
        Assert.Equal(Mastery.EasyStep, Mastery.StepFor(0));
        Assert.Equal(Mastery.HardStep, Mastery.StepFor(7));
        Assert.Equal(62.5, Mastery.Of([Answer(true, difficulty: null)], Monday), Precision);
    }

    // The example of A40 in docs/spec/acceptance.md: five topics with the same start value.
    [Fact]
    public void FiveTopics_FromTheAcceptanceCriterion()
    {
        var now = Monday.AddMinutes(1);
        var easyRight = Mastery.Of([Answer(true, Monday, difficulty: 1)], now);
        var hardRight = Mastery.Of([Answer(true, Monday, difficulty: 3)], now);
        var wrong = Mastery.Of([Answer(false, Monday)], now);
        var wrongThenRight = Mastery.Of([Answer(false, Monday), Answer(true, now)], now);
        var rightThenWrong = Mastery.Of([Answer(true, Monday), Answer(false, now)], now);

        Assert.True(easyRight > Mastery.Start);
        Assert.True(hardRight > easyRight);
        Assert.True(wrong < Mastery.Start);
        Assert.True(wrongThenRight > rightThenWrong);
    }

    // 50 -> 62.5 -> 71.875 -> 53.90625, a tick apart so nothing decays.
    [Fact]
    public void EveryAnswer_MovesItFromWhereItIs()
    {
        var answers = new[] { true, true, false }.Select((correct, tick) => Answer(correct, Monday.AddTicks(tick)));

        Assert.Equal(53.90625, Mastery.Of(answers, Monday.AddTicks(2)), Precision);
        Assert.Equal(54, Mastery.Percent(Mastery.Of(answers, Monday.AddTicks(2))));
    }

    // 62.5 halves every 90 days without an answer.
    [Fact]
    public void WithoutReview_ItHalvesEveryHalfLife()
    {
        var answer = Answer(correct: true);

        Assert.Equal(62.5, Mastery.Of([answer], Monday), Precision);
        Assert.Equal(31.25, Mastery.Of([answer], Monday.AddDays(90)), Precision);
        Assert.Equal(15.625, Mastery.Of([answer], Monday.AddDays(180)), Precision);
        Assert.Equal(44.19417382, Mastery.Of([answer], Monday.AddDays(45)), 8);
    }

    // A41: answers over several weeks, then weeks without review. The value has dropped, and below the start value of
    // a topic never practised, which is on purpose.
    [Fact]
    public void WeeksWithoutReview_LowerIt()
    {
        var answers = Enumerable.Range(0, 4).Select(week => Answer(true, Monday.AddDays(7 * week))).ToList();
        var lastAnswer = Monday.AddDays(21);

        var right = Mastery.Of(answers, lastAnswer);
        var weekLater = Mastery.Of(answers, lastAnswer.AddDays(7));
        var monthsLater = Mastery.Of(answers, lastAnswer.AddDays(120));

        Assert.True(weekLater < right);
        Assert.True(monthsLater < weekLater);
        Assert.True(monthsLater < Mastery.Start);
    }

    // The first answer has decayed for 90 days when the second comes: 62.5 -> 31.25 -> 48.4375. On the same day the
    // two would give 71.875.
    [Fact]
    public void OlderAnswers_HaveDecayedBeforeNewerOnesCount()
    {
        var later = Monday.AddDays(90);

        Assert.Equal(48.4375, Mastery.Of([Answer(true, Monday), Answer(true, later)], later), Precision);
        Assert.Equal(71.875, Mastery.Of([Answer(true, later), Answer(true, later.AddTicks(1))], later.AddTicks(1)), Precision);
    }

    // Newer answers weigh more: the same wrong answer weeks ago leaves the value higher than one today.
    [Fact]
    public void NewerAnswers_WeighMore()
    {
        var now = Monday.AddDays(28);
        var wrongLongAgo = Mastery.Of([Answer(false, Monday), Answer(true, now)], now);
        var wrongToday = Mastery.Of([Answer(true, Monday), Answer(false, now)], now);

        Assert.True(wrongLongAgo > wrongToday);
    }

    // Clocks differ a bit: an answer stamped after now counts, but time doesn't run backwards for it.
    [Fact]
    public void AnswerAfterNow_DoesNotDecayBackwards()
    {
        Assert.Equal(62.5, Mastery.Of([Answer(true, Monday.AddMinutes(5))], Monday), Precision);
    }

    [Fact]
    public void ManyEqualAnswers_AndLongDecay_StayWithinTheScale()
    {
        var allRight = Answers(200, correct: true, difficulty: 3);
        var allWrong = Answers(200, correct: false, difficulty: 3);
        var end = Monday.AddMinutes(199);

        Assert.InRange(Mastery.Of(allRight, end), 0, 100);
        Assert.InRange(Mastery.Of(allWrong, end), 0, 100);
        Assert.Equal(100, Mastery.Percent(Mastery.Of(allRight, end)));
        Assert.Equal(0, Mastery.Percent(Mastery.Of(allWrong, end)));
        Assert.InRange(Mastery.Of(allRight, end.AddYears(10)), 0, 100);
        Assert.Equal(0, Mastery.Percent(Mastery.Of(allRight, end.AddYears(10))));
    }

    // The order comes from the answers, not from when their events arrived.
    [Fact]
    public void OrderOfTheRecords_DoesNotMatter()
    {
        var first = Answer(false, Monday);
        var second = Answer(true, Monday.AddDays(3));
        var sameTime = new[] { Answer(true, Monday), Answer(false, Monday) };
        var now = Monday.AddDays(10);

        Assert.Equal(Mastery.Of([first, second], now), Mastery.Of([second, first], now));
        Assert.Equal(Mastery.Of(sameTime, now), Mastery.Of(sameTime.Reverse(), now));
    }

    // 62.5 is shown as 63, like commercial rounding.
    [Fact]
    public void Percent_RoundsHalfAwayFromZero()
    {
        Assert.Equal(63, Mastery.Percent(62.5));
        Assert.Equal(38, Mastery.Percent(37.5));
        Assert.Equal(54, Mastery.Percent(53.90625));
    }

    // Response time and confidence are kept with the record, but nothing weighs them (#360).
    [Fact]
    public void ResponseTimeAndConfidence_DoNotMoveIt()
    {
        var quickAndSure = LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), null, true, 2, 300, 4, Mode.Practice, Monday);
        var slowAndUnsure = LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), null, true, 2, 120_000, 1, Mode.Practice, Monday);

        Assert.Equal(Mastery.Of([quickAndSure], Monday), Mastery.Of([slowAndUnsure], Monday));
        Assert.Equal(Mastery.Of([Answer(true)], Monday), Mastery.Of([quickAndSure], Monday));
    }

    private static IEnumerable<LearningRecord> Answers(int count, bool correct, int difficulty) =>
        Enumerable.Range(0, count).Select(minute => Answer(correct, Monday.AddMinutes(minute), difficulty)).ToList();

    private static LearningRecord Answer(bool correct, DateTime? answeredAt = null, int? difficulty = 2) =>
        LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), null, correct, difficulty, 4200, null, Mode.Practice, answeredAt ?? Monday);
}