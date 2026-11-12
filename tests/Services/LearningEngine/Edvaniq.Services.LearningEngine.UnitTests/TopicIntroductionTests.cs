using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// Start value and introduction (#363): before it a topic is new at its start value, after it the answers count, the
// check questions of the introducing session included. Without an introduction the transition until #396 applies.
public sealed class TopicIntroductionTests
{
    private const int Precision = 9;

    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Unit = Guid.NewGuid();

    // AK1
    [Fact]
    public void NewTopicWithoutAnswers_HasTheDefaultStartValue_AndIsNew()
    {
        Assert.Equal(new TopicStanding(50, TopicState.New), TopicStates.Assess([], null, Monday));
    }

    [Fact]
    public void IntroducedTopicWithoutAnswers_StaysAtTheStartValue_AndIsBuilding()
    {
        Assert.Equal(new TopicStanding(50, TopicState.Building), TopicStates.Assess([], Introduction(Monday), Monday));
    }

    // AK2: the answer of the session that introduced the topic and one after it count, one from before doesn't.
    [Fact]
    public void AfterTheIntroduction_AnswersCount_TheOnesBeforeDoNot()
    {
        var before = Answer(true, Monday.AddDays(-1), Guid.NewGuid());
        var checkQuestion = Answer(true, Monday.AddMinutes(-5), Unit);
        var after = Answer(false, Monday.AddMinutes(10), null);
        var introduction = Introduction(Monday);
        var now = Monday.AddMinutes(10);

        Assert.False(introduction.Counts(before));
        Assert.True(introduction.Counts(checkQuestion));
        Assert.True(introduction.Counts(after));
        Assert.Equal(
            Mastery.Percent(Mastery.Of([checkQuestion, after], now)),
            TopicStates.Assess([before, checkQuestion, after], introduction, now).Mastery);
    }

    // Answers given only before the introduction leave the topic at its start value once it's introduced.
    [Fact]
    public void AnswersOnlyBeforeTheIntroduction_LeaveTheStartValue()
    {
        var before = Answer(true, Monday.AddDays(-1), Guid.NewGuid());

        Assert.Equal(63, TopicStates.Assess([before], null, Monday.AddDays(-1)).Mastery);
        Assert.Equal(new TopicStanding(50, TopicState.Building), TopicStates.Assess([before], Introduction(Monday), Monday));
    }

    // The transition until #396: no introduction, so the first record introduces the topic and every record counts.
    [Fact]
    public void WithoutIntroduction_UntilSessionsExist_EveryRecordCounts()
    {
        var records = new List<LearningRecord> { Answer(true, Monday, Guid.NewGuid()), Answer(true, Monday, null) };

        var (introduced, counted) = TopicStates.IntroducedByFirstRecord(records);

        Assert.True(introduced);
        Assert.Equal(records, counted);
        Assert.Equal(
            new TopicStanding(Mastery.Percent(Mastery.Of(records, Monday)), TopicState.Building),
            TopicStates.Assess(records, null, Monday));
        Assert.False(TopicStates.IntroducedByFirstRecord([]).Introduced);
    }

    // AK3: a start value of the user's own replaces the default, every step stays the same: a correct medium answer
    // moves a quarter of the way to 100 from either.
    [Fact]
    public void OwnStartValue_ReplacesTheDefault_WithTheSameRule()
    {
        var right = Answer(true, Monday, null);
        var wrong = Answer(false, Monday, null);

        Assert.Equal(62.5, Mastery.Of([right], Monday), Precision);
        Assert.Equal(77.5, Mastery.Of([right], Monday, start: 70), Precision);
        Assert.Equal(52.5, Mastery.Of([wrong], Monday, start: 70), Precision);
        Assert.Equal(70, Mastery.Of([], Monday, start: 70), Precision);
        Assert.Equal(Mastery.Start, Mastery.Of([], Monday), Precision);
        Assert.Equal(new TopicStanding(70, TopicState.New), TopicStates.Assess([], null, Monday, start: 70));
        Assert.Equal(new TopicStanding(78, TopicState.Building), TopicStates.Assess([right], Introduction(Monday), Monday, 70));
    }

    // It decays like any value once there are answers (60 -> 70 -> 35 after 90 days), and stays within 0 to 100.
    [Fact]
    public void OwnStartValue_DecaysAndIsClamped()
    {
        Assert.Equal(35, Mastery.Of([Answer(true, Monday, null)], Monday.AddDays(90), start: 60), Precision);
        Assert.Equal(100, Mastery.Of([], Monday, start: 120));
        Assert.Equal(0, Mastery.Of([], Monday, start: -5));
    }

    private static TopicIntroduction Introduction(DateTime at) => TopicIntroduction.Create("alice", Guid.Empty, Unit, at);

    private static LearningRecord Answer(bool correct, DateTime answeredAt, Guid? session) =>
        LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), session, correct, 2, 4200, null, Mode.Practice, answeredAt);
}